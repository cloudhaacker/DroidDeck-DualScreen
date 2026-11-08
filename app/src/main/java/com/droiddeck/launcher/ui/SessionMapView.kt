package com.droiddeck.launcher.ui

import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.RectF
import android.view.GestureDetector
import android.view.MotionEvent
import android.view.ScaleGestureDetector
import android.view.View
import com.droiddeck.launcher.R
import com.droiddeck.launcher.session.MapFeed

internal class SessionMapView(context: Context) : View(context) {
    private val density = resources.displayMetrics.density
    private val bitmapPaint = Paint(Paint.FILTER_BITMAP_FLAG)
    private val textPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.WHITE
        textSize = dp(14).toFloat()
    }
    private val placePaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = Color.WHITE }
    private val creditPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.rgb(140, 148, 160)
        textSize = dp(13).toFloat()
    }
    private val gridPaint = Paint().apply {
        color = Color.rgb(40, 48, 58)
        strokeWidth = 1f
    }
    private val dest = RectF()

    private fun dp(value: Int): Int = (value * density + 0.5f).toInt()

    // Touch: drag pans, pinch zooms, double tap goes back to the start view. The game side does the work and sends a new picture.
    private var pendingZoom = 1f
    private var pendingDx = 0f
    private var pendingDy = 0f
    private var lastSent = 0L

    private val gestures = GestureDetector(context, object : GestureDetector.SimpleOnGestureListener() {
        override fun onDown(e: MotionEvent): Boolean = true

        override fun onScroll(e1: MotionEvent?, e2: MotionEvent, distanceX: Float, distanceY: Float): Boolean {
            if (dest.width() > 0f && dest.height() > 0f) {
                pendingDx -= distanceX / dest.width()
                pendingDy -= distanceY / dest.height()
                flush(false)
            }
            return true
        }

        override fun onDoubleTap(e: MotionEvent): Boolean {
            MapFeed.sendCommand("view:reset")
            return true
        }
    })

    private val scaler = ScaleGestureDetector(context, object : ScaleGestureDetector.SimpleOnScaleGestureListener() {
        override fun onScale(detector: ScaleGestureDetector): Boolean {
            pendingZoom *= detector.scaleFactor
            flush(false)
            return true
        }
    })

    private fun flush(force: Boolean) {
        val now = android.os.SystemClock.uptimeMillis()
        if (!force && now - lastSent < 70) return
        lastSent = now
        if (pendingZoom != 1f) {
            MapFeed.sendCommand(String.format(java.util.Locale.US, "zoom:%.3f", pendingZoom))
            pendingZoom = 1f
        }
        if (pendingDx != 0f || pendingDy != 0f) {
            MapFeed.sendCommand(String.format(java.util.Locale.US, "pan:%.4f,%.4f", pendingDx, pendingDy))
            pendingDx = 0f
            pendingDy = 0f
        }
    }

    override fun onTouchEvent(event: MotionEvent): Boolean {
        if (MapFeed.bitmap == null || MapFeed.idle) return false
        scaler.onTouchEvent(event)
        if (!scaler.isInProgress) gestures.onTouchEvent(event)
        if (event.actionMasked == MotionEvent.ACTION_UP || event.actionMasked == MotionEvent.ACTION_CANCEL) flush(true)
        return true
    }

    override fun onDraw(canvas: Canvas) {
        canvas.drawColor(Color.BLACK)
        if (MapFeed.idle && MapFeed.connected) {
            drawIdle(canvas)
            return
        }
        val bitmap = MapFeed.bitmap
        if (bitmap == null) {
            drawWaiting(canvas)
            return
        }
        val scale = minOf(width.toFloat() / bitmap.width, height.toFloat() / bitmap.height)
        val w = bitmap.width * scale
        val h = bitmap.height * scale
        dest.set((width - w) / 2f, (height - h) / 2f, (width + w) / 2f, (height + h) / 2f)
        canvas.drawBitmap(bitmap, null, dest, bitmapPaint)
    }

    private fun drawIdle(canvas: Canvas) {
        canvas.drawColor(Color.rgb(20, 23, 28))
        val place = MapFeed.place
        val centre = height / 2f
        if (place.isNotEmpty()) {
            placePaint.textSize = dp(30).toFloat()
            val room = width * 0.88f
            val measured = placePaint.measureText(place)
            if (measured > room) placePaint.textSize = maxOf(dp(14).toFloat(), placePaint.textSize * room / measured)
            canvas.drawText(place, (width - placePaint.measureText(place)) / 2f, centre, placePaint)
            return
        }
        placePaint.textSize = dp(26).toFloat()
        val credit = context.getString(R.string.second_idle_credit)
        canvas.drawText(credit, (width - placePaint.measureText(credit)) / 2f, centre, placePaint)
        val name = context.getString(R.string.second_idle_mod)
        canvas.drawText(name, (width - creditPaint.measureText(name)) / 2f, centre + dp(30).toFloat(), creditPaint)
    }

    private fun drawWaiting(canvas: Canvas) {
        val step = dp(48).toFloat()
        var x = 0f
        while (x <= width) {
            canvas.drawLine(x, 0f, x, height.toFloat(), gridPaint)
            x += step
        }
        var y = 0f
        while (y <= height) {
            canvas.drawLine(0f, y, width.toFloat(), y, gridPaint)
            y += step
        }
        val lines = listOf(
            context.getString(R.string.second_mode_game_map),
            context.getString(R.string.second_map_waiting),
            "$width×$height",
        )
        var baseline = height / 2f - dp(20)
        for (line in lines) {
            canvas.drawText(line, (width - textPaint.measureText(line)) / 2f, baseline, textPaint)
            baseline += dp(24)
        }
    }
}
