package com.droiddeck.launcher.ui

import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.RectF
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
    private val gridPaint = Paint().apply {
        color = Color.rgb(40, 48, 58)
        strokeWidth = 1f
    }
    private val dest = RectF()

    private fun dp(value: Int): Int = (value * density + 0.5f).toInt()

    override fun onDraw(canvas: Canvas) {
        canvas.drawColor(Color.BLACK)
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
        val title = MapFeed.title
        if (title.isNotEmpty()) canvas.drawText(title, dp(8).toFloat(), dp(20).toFloat(), textPaint)
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
