package com.droiddeck.launcher.session

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.util.Log
import com.droiddeck.launcher.core.SessionPart
import java.io.DataInputStream
import java.io.DataOutputStream
import java.io.IOException
import java.net.InetAddress
import java.net.ServerSocket
import java.net.Socket
import java.util.concurrent.ExecutorService
import java.util.concurrent.Executors

object MapFeed {
    @Volatile var bitmap: Bitmap? = null
    @Volatile var title: String = ""
    @Volatile var connected: Boolean = false
    @Volatile var onChange: (() -> Unit)? = null
    @Volatile var sender: ((String) -> Unit)? = null

    fun sendCommand(command: String) {
        sender?.invoke(command)
    }

    fun reset() {
        bitmap = null
        title = ""
        connected = false
        onChange?.invoke()
    }
}

/**
 * Loopback server a game mod connects to. Every message is a big-endian u32 length (type byte plus payload),
 * a type byte, then the payload: 1 = PNG or JPEG frame, 2 = UTF-8 title. Messages to the game use type 3 (UTF-8 command).
 */
class MapBridge : SessionPart() {
    @Volatile private var server: ServerSocket? = null
    @Volatile private var client: Socket? = null
    private var writer: ExecutorService? = null

    override fun start() {
        val socket = runCatching { ServerSocket(PORT, 1, InetAddress.getByName("127.0.0.1")) }
            .onFailure { Log.w(TAG, "map bridge unavailable on port $PORT", it) }
            .getOrNull() ?: return
        server = socket
        writer = Executors.newSingleThreadExecutor { task -> Thread(task, "map-bridge-writer").apply { isDaemon = true } }
        Thread({ serve(socket) }, "map-bridge").apply { isDaemon = true; start() }
        Log.i(TAG, "map bridge listening on 127.0.0.1:$PORT")
    }

    override fun stop() {
        MapFeed.sender = null
        runCatching { client?.close() }
        runCatching { server?.close() }
        writer?.shutdownNow()
        writer = null
        MapFeed.reset()
    }

    private fun serve(socket: ServerSocket) {
        while (!socket.isClosed) {
            val peer = runCatching { socket.accept() }.getOrNull() ?: break
            runCatching { client?.close() }
            client = peer
            Thread({ read(peer) }, "map-bridge-client").apply { isDaemon = true; start() }
        }
    }

    private fun read(peer: Socket) {
        val input = DataInputStream(peer.getInputStream().buffered())
        val output = DataOutputStream(peer.getOutputStream())
        MapFeed.connected = true
        MapFeed.sender = { text -> writer?.execute { write(output, text) } }
        MapFeed.onChange?.invoke()
        Log.i(TAG, "map bridge: game connected")
        try {
            while (true) {
                val length = input.readInt()
                if (length < 1 || length > MAX_MESSAGE) break
                val type = input.readUnsignedByte()
                val payload = ByteArray(length - 1)
                input.readFully(payload)
                when (type) {
                    TYPE_IMAGE -> BitmapFactory.decodeByteArray(payload, 0, payload.size)?.let {
                        MapFeed.bitmap = it
                        MapFeed.onChange?.invoke()
                    }
                    TYPE_TITLE -> {
                        MapFeed.title = String(payload, Charsets.UTF_8)
                        MapFeed.onChange?.invoke()
                    }
                }
            }
        } catch (e: IOException) {
            Log.d(TAG, "map bridge: game disconnected (${e.message})")
        } finally {
            runCatching { peer.close() }
            if (client === peer) {
                client = null
                MapFeed.sender = null
                MapFeed.connected = false
                MapFeed.onChange?.invoke()
            }
        }
    }

    private fun write(output: DataOutputStream, text: String) {
        val bytes = text.toByteArray(Charsets.UTF_8)
        try {
            synchronized(output) {
                output.writeInt(1 + bytes.size)
                output.writeByte(TYPE_COMMAND)
                output.write(bytes)
                output.flush()
            }
        } catch (e: IOException) {
            Log.d(TAG, "map bridge: command not sent (${e.message})")
        }
    }

    companion object {
        const val PORT = 47200
        private const val TAG = "MapBridge"
        private const val MAX_MESSAGE = 16 shl 20
        private const val TYPE_IMAGE = 1
        private const val TYPE_TITLE = 2
        private const val TYPE_COMMAND = 3
    }
}
