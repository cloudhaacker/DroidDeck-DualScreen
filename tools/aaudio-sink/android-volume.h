/* SPDX-License-Identifier: LGPL-2.1-or-later */
#include <stdio.h>
#include <pulsecore/core-rtclock.h>
#include <pulsecore/core-subscribe.h>

#define ANDROID_VOLUME_IDLE_MS 100
#define ANDROID_VOLUME_ACTIVE_MS 16
#define ANDROID_VOLUME_ACTIVE_TICKS 40

struct android_volume {
    pa_sink *sink;
    char *directory;
    pa_time_event *timer;
    unsigned active_ticks;
    double gain;
};

/* An optional fourth field is a linear gain the sink applies itself, for devices whose
 * Android does not apply the media volume to DroidDeck's streams; it is absent otherwise. */
static bool android_volume_read(const char *directory, unsigned *level, unsigned *maximum, unsigned *muted, double *gain) {
    char *path = pa_sprintf_malloc("%s/volume.state", directory);
    FILE *file = fopen(path, "r");
    pa_xfree(path);
    *gain = -1;
    if (!file) return false;
    int fields = fscanf(file, "%u %u %u %lf", level, maximum, muted, gain);
    fclose(file);
    if (fields != 4 || !(*gain >= 0)) *gain = -1;
    else if (*gain > 1) *gain = 1;
    return fields >= 3 && *maximum > 0 && *level <= *maximum && *muted <= 1;
}

static void android_volume_soft(pa_sink *sink, double gain, pa_cvolume *volume) {
    if (gain < 0) pa_cvolume_reset(volume, sink->sample_spec.channels);
    else pa_cvolume_set(volume, sink->sample_spec.channels, pa_sw_volume_from_linear(gain));
}

static void android_volume_request(pa_sink *sink, const char *name, unsigned value) {
    struct userdata *u = sink->userdata;
    if (!u->android_volume || sink->state == PA_SINK_INIT) return;
    char *path = pa_sprintf_malloc("%s/%s.request", u->android_volume->directory, name);
    char *staged = pa_sprintf_malloc("%s.tmp", path);
    FILE *file = fopen(staged, "w");
    if (file) {
        fprintf(file, "%u\n", value);
        if (fclose(file) == 0 && rename(staged, path) < 0) pa_log_warn("Could not publish Android %s", name);
    }
    pa_xfree(staged);
    pa_xfree(path);
}

static void android_volume_set(pa_sink *sink) {
    struct userdata *u = sink->userdata;
    unsigned level, maximum, muted;
    double gain;
    if (!u->android_volume || !android_volume_read(u->android_volume->directory, &level, &maximum, &muted, &gain)) return;
    unsigned requested = PA_MIN(pa_cvolume_max(&sink->real_volume), PA_VOLUME_NORM);
    level = ((uint64_t)requested * maximum + PA_VOLUME_NORM / 2) / PA_VOLUME_NORM;
    pa_cvolume_set(&sink->real_volume, sink->sample_spec.channels, (uint64_t)level * PA_VOLUME_NORM / maximum);
    android_volume_soft(sink, u->android_volume->gain, &sink->soft_volume);
    pa_log_info("android volume: Linux side set the sink to %u/%u, asking Android for it", level, maximum);
    android_volume_request(sink, "volume", level);
}

static void android_volume_mute(pa_sink *sink) {
    pa_log_info("android volume: Linux side set mute %d, asking Android for it", sink->muted ? 1 : 0);
    android_volume_request(sink, "mute", sink->muted ? 1 : 0);
}

static void android_volume_refresh(pa_mainloop_api *api, pa_time_event *event, const struct timeval *tv, void *userdata) {
    struct android_volume *bridge = userdata;
    unsigned level, maximum, muted;
    double gain;
    char *request = pa_sprintf_malloc("%s/volume.request", bridge->directory);
    char *mute_request = pa_sprintf_malloc("%s/mute.request", bridge->directory);
    char *processing = pa_sprintf_malloc("%s.processing", request);
    char *mute_processing = pa_sprintf_malloc("%s.processing", mute_request);
    if (access(request, F_OK) != 0 && access(mute_request, F_OK) != 0 &&
        access(processing, F_OK) != 0 && access(mute_processing, F_OK) != 0) {
        if (android_volume_read(bridge->directory, &level, &maximum, &muted, &gain)) {
            pa_cvolume volume;
            bool changed = false;
            if (bridge->sink->n_volume_steps != maximum + 1) {
                bridge->sink->n_volume_steps = maximum + 1;
                pa_subscription_post(bridge->sink->core, PA_SUBSCRIPTION_EVENT_SINK | PA_SUBSCRIPTION_EVENT_CHANGE, bridge->sink->index);
            }
            pa_cvolume_set(&volume, bridge->sink->sample_spec.channels, (uint64_t)level * PA_VOLUME_NORM / maximum);
            if (!pa_cvolume_equal(&volume, &bridge->sink->real_volume)) {
                pa_sink_volume_changed(bridge->sink, &volume);
                changed = true;
            }
            if (bridge->sink->muted != !!muted) {
                pa_sink_mute_changed(bridge->sink, !!muted);
                changed = true;
            }
            if (gain != bridge->gain) {
                pa_cvolume soft;
                bridge->gain = gain;
                android_volume_soft(bridge->sink, gain, &soft);
                pa_sink_set_soft_volume(bridge->sink, &soft);
                pa_log_info("android volume: DroidDeck applies gain %.4f itself", gain);
                changed = true;
            }
            if (changed) {
                bridge->active_ticks = ANDROID_VOLUME_ACTIVE_TICKS;
                pa_log_info("android volume: Android is at %u/%u muted %u, sink follows", level, maximum, muted);
            }
        }
    } else {
        bridge->active_ticks = ANDROID_VOLUME_ACTIVE_TICKS;
    }
    pa_xfree(request);
    pa_xfree(mute_request);
    pa_xfree(processing);
    pa_xfree(mute_processing);
    unsigned delay_ms = ANDROID_VOLUME_IDLE_MS;
    if (bridge->active_ticks) {
        bridge->active_ticks--;
        delay_ms = ANDROID_VOLUME_ACTIVE_MS;
    }
    pa_core_rttime_restart(bridge->sink->core, event, pa_rtclock_now() + delay_ms * PA_USEC_PER_MSEC);
}

static struct android_volume *android_volume_new(pa_sink *sink, const char *directory) {
    unsigned level, maximum, muted;
    double gain;
    if (!directory) return NULL;
    struct android_volume *bridge = pa_xnew0(struct android_volume, 1);
    bridge->sink = sink;
    bridge->directory = pa_xstrdup(directory);
    bridge->gain = -1;
    if (android_volume_read(directory, &level, &maximum, &muted, &gain)) {
        pa_cvolume_set(&sink->real_volume, sink->sample_spec.channels, (uint64_t)level * PA_VOLUME_NORM / maximum);
        sink->reference_volume = sink->real_volume;
        sink->muted = !!muted;
        sink->n_volume_steps = maximum + 1;
        bridge->gain = gain;
    }
    android_volume_soft(sink, bridge->gain, &sink->soft_volume);
    pa_sink_set_set_volume_callback(sink, android_volume_set);
    pa_sink_set_set_mute_callback(sink, android_volume_mute);
    bridge->timer = pa_core_rttime_new(sink->core, pa_rtclock_now() + ANDROID_VOLUME_IDLE_MS * PA_USEC_PER_MSEC,
                                     android_volume_refresh, bridge);
    return bridge;
}

static void android_volume_free(struct android_volume *bridge) {
    if (!bridge) return;
    bridge->sink->core->mainloop->time_free(bridge->timer);
    pa_xfree(bridge->directory);
    pa_xfree(bridge);
}
