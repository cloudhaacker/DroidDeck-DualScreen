package com.droiddeck.launcher.ui

import android.content.Context
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Rect
import android.graphics.RectF
import android.os.SystemClock
import android.view.GestureDetector
import android.view.MotionEvent
import android.view.ScaleGestureDetector
import android.view.SurfaceView
import android.view.View
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import com.droiddeck.launcher.R
import com.droiddeck.launcher.session.SessionPrefs
import com.droiddeck.launcher.session.SessionState
import java.util.Locale

/** Per-game screen zoom settings. Centre is a fraction of the game picture; zoom 1 shows the widest view that fills the bottom screen. */
internal object ScreenZoom {
    var centreX = 0.5f
    var centreY = 0.5f
    var zoom = 1f
    var sharp by mutableStateOf(false)
    var fps by mutableStateOf(30)
    private var loadedKey: String? = null

    fun key(): String = SessionState.gameId ?: "general"

    fun load(context: Context) {
        val key = key()
        if (loadedKey == key) return
        loadedKey = key
        centreX = 0.5f; centreY = 0.5f; zoom = 1f; sharp = false; fps = 30
        val parts = SessionPrefs.screenZoom(context, key)?.split(",") ?: return
        parts.getOrNull(0)?.toFloatOrNull()?.let { centreX = it }
        parts.getOrNull(1)?.toFloatOrNull()?.let { centreY = it }
        parts.getOrNull(2)?.toFloatOrNull()?.let { zoom = it }
        parts.getOrNull(3)?.let { sharp = it == "1" }
        parts.getOrNull(4)?.toIntOrNull()?.let { fps = if (it >= 60) 60 else 30 }
    }

    fun save(context: Context) {
        SessionPrefs.setScreenZoom(context, key(),
            String.format(Locale.US, "%.5f,%.5f,%.4f,%d,%d", centreX, centreY, zoom, if (sharp) 1 else 0, fps))
    }
}

internal class ScreenZoomView(context: Context, private val source: () -> SurfaceView?) : View(context) {
    private val density = resources.displayMetrics.density
    private val paint = Paint()
    private val textPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.WHITE
        textSize = 14 * density
    }
    private val hintPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.WHITE
        textSize = 13 * density
        setShadowLayer(4 * density, 0f, 2 * density, Color.BLACK)
    }
    private val full = RectF()
    private val src = Rect()
    private var shown: Bitmap? = null
    private var spare: Bitmap? = null
    private var copying = false
    private var running = false
    private var frameW = 0
    private var frameH = 0
    private var hintUntil = 0L
    private var before: Triple<Float, Float, Float>? = null

    init {
        ScreenZoom.load(context)
    }

    override fun onAttachedToWindow() {
        super.onAttachedToWindow()
        running = true
        hintUntil = SystemClock.uptimeMillis() + HINT_MS
        post(tick)
    }

    override fun onDetachedFromWindow() {
        running = false
        removeCallbacks(tick)
        super.onDetachedFromWindow()
    }

    private val tick = object : Runnable {
        override fun run() {
            if (!running) return
            capture()
            postDelayed(this, 1000L / ScreenZoom.fps)
        }
    }

    /** The widest source rectangle with the bottom screen's shape that fits inside the game picture, divided by the zoom. */
    private fun sourceSize(fw: Int, fh: Int): Pair<Float, Float> {
        val aspect = width.toFloat() / height
        val baseW = minOf(fw.toFloat(), fh * aspect)
        val w = baseW / ScreenZoom.zoom
        return w to w / aspect
    }

    private fun clampCentre(fw: Int, fh: Int) {
        val (w, h) = sourceSize(fw, fh)
        val halfX = w / 2f / fw
        val halfY = h / 2f / fh
        ScreenZoom.centreX = ScreenZoom.centreX.coerceIn(halfX, maxOf(halfX, 1f - halfX))
        ScreenZoom.centreY = ScreenZoom.centreY.coerceIn(halfY, maxOf(halfY, 1f - halfY))
    }

    private fun capture() {
        if (copying || width <= 0 || height <= 0) return
        val view = source() ?: return
        val fw = view.width
        val fh = view.height
        if (fw <= 0 || fh <= 0 || view.holder.surface?.isValid != true) return
        frameW = fw
        frameH = fh
        clampCentre(fw, fh)
        val (w, h) = sourceSize(fw, fh)
        val left = (ScreenZoom.centreX * fw - w / 2f).toInt().coerceIn(0, fw - 1)
        val top = (ScreenZoom.centreY * fh - h / 2f).toInt().coerceIn(0, fh - 1)
        src.set(left, top, minOf(fw, left + maxOf(1, w.toInt())), minOf(fh, top + maxOf(1, h.toInt())))
        // Copy at the source's own size unless it is larger than the bottom screen; the canvas does the enlarging.
        val shrink = minOf(1f, width.toFloat() / src.width(), height.toFloat() / src.height())
        val bw = maxOf(1, (src.width() * shrink).toInt())
        val bh = maxOf(1, (src.height() * shrink).toInt())
        val target = spare?.takeIf { it.width == bw && it.height == bh } ?: try {
            spare?.recycle()
            Bitmap.createBitmap(bw, bh, Bitmap.Config.ARGB_8888).also { spare = it }
        } catch (_: OutOfMemoryError) {
            return
        }
        val main = handler ?: return
        copying = true
        try {
            android.view.PixelCopy.request(view, Rect(src), target, { result ->
                copying = false
                if (result == android.view.PixelCopy.SUCCESS && running) {
                    spare = shown
                    shown = target
                    invalidate()
                }
            }, main)
        } catch (_: IllegalArgumentException) {
            copying = false
        }
    }

    private val gestures = GestureDetector(context, object : GestureDetector.SimpleOnGestureListener() {
        override fun onDown(e: MotionEvent): Boolean = true

        override fun onScroll(e1: MotionEvent?, e2: MotionEvent, distanceX: Float, distanceY: Float): Boolean {
            if (frameW <= 0 || frameH <= 0) return true
            val (w, h) = sourceSize(frameW, frameH)
            ScreenZoom.centreX += distanceX / width * w / frameW
            ScreenZoom.centreY += distanceY / height * h / frameH
            clampCentre(frameW, frameH)
            return true
        }

        override fun onDoubleTap(e: MotionEvent): Boolean {
            val back = before
            if (ScreenZoom.zoom > 1.01f || back == null) {
                before = Triple(ScreenZoom.centreX, ScreenZoom.centreY, ScreenZoom.zoom)
                ScreenZoom.zoom = 1f
            } else {
                ScreenZoom.centreX = back.first
                ScreenZoom.centreY = back.second
                ScreenZoom.zoom = back.third
                before = null
            }
            if (frameW > 0) clampCentre(frameW, frameH)
            ScreenZoom.save(context)
            return true
        }
    })

    private val scaler = ScaleGestureDetector(context, object : ScaleGestureDetector.SimpleOnScaleGestureListener() {
        override fun onScale(detector: ScaleGestureDetector): Boolean {
            if (frameW <= 0 || frameH <= 0) return true
            val (oldW, oldH) = sourceSize(frameW, frameH)
            val fx = detector.focusX / width - 0.5f
            val fy = detector.focusY / height - 0.5f
            // Keep the game point under the fingers in place while the zoom changes.
            val pointX = ScreenZoom.centreX + fx * oldW / frameW
            val pointY = ScreenZoom.centreY + fy * oldH / frameH
            ScreenZoom.zoom = (ScreenZoom.zoom * detector.scaleFactor).coerceIn(1f, MAX_ZOOM)
            val (newW, newH) = sourceSize(frameW, frameH)
            ScreenZoom.centreX = pointX - fx * newW / frameW
            ScreenZoom.centreY = pointY - fy * newH / frameH
            clampCentre(frameW, frameH)
            before = null
            return true
        }
    })

    override fun onTouchEvent(event: MotionEvent): Boolean {
        scaler.onTouchEvent(event)
        if (!scaler.isInProgress) gestures.onTouchEvent(event)
        if (event.actionMasked == MotionEvent.ACTION_UP || event.actionMasked == MotionEvent.ACTION_CANCEL) ScreenZoom.save(context)
        return true
    }

    override fun onDraw(canvas: Canvas) {
        canvas.drawColor(Color.BLACK)
        val bitmap = shown
        if (bitmap == null) {
            val line = context.getString(R.string.second_zoom_waiting)
            canvas.drawText(line, (width - textPaint.measureText(line)) / 2f, height / 2f, textPaint)
            return
        }
        paint.isFilterBitmap = !ScreenZoom.sharp
        full.set(0f, 0f, width.toFloat(), height.toFloat())
        canvas.drawBitmap(bitmap, null, full, paint)
        if (SystemClock.uptimeMillis() < hintUntil) {
            val hint = context.getString(R.string.second_zoom_hint)
            canvas.drawText(hint, (width - hintPaint.measureText(hint)) / 2f, height - 24 * density, hintPaint)
        }
    }

    private companion object {
        const val MAX_ZOOM = 16f
        const val HINT_MS = 4000L
    }
}
