using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using MelonLoader;

namespace DualScreenBridge
{
    internal sealed class BridgeClient
    {
        public const string Host = "127.0.0.1";
        public const int Port = 47200;
        private const int TypeImage = 1;
        private const int TypeTitle = 2;
        private const int TypeCommand = 3;

        private readonly object gate = new object();
        private readonly Queue<string> commands = new Queue<string>();
        private byte[] frame;
        private string title;
        private string lastTitle;
        private volatile bool running;
        private volatile bool connected;
        private volatile bool readerDone;

        public bool Connected { get { return connected; } }

        public void Start()
        {
            running = true;
            Thread worker = new Thread(Run);
            worker.IsBackground = true;
            worker.Name = "dualscreen-bridge";
            worker.Start();
        }

        public void Stop()
        {
            running = false;
            lock (gate) Monitor.PulseAll(gate);
        }

        public void SetFrame(byte[] jpg)
        {
            lock (gate) { frame = jpg; Monitor.PulseAll(gate); }
        }

        public void SetTitle(string text)
        {
            lock (gate) { title = text; lastTitle = text; Monitor.PulseAll(gate); }
        }

        public bool TryTakeCommand(out string command)
        {
            lock (gate)
            {
                if (commands.Count > 0) { command = commands.Dequeue(); return true; }
            }
            command = null;
            return false;
        }

        private void Run()
        {
            while (running)
            {
                try { Session(); }
                catch (Exception e) { MelonLogger.Msg("bridge not connected: " + e.Message); }
                connected = false;
                for (int i = 0; i < 30 && running; i++) Thread.Sleep(100);
            }
        }

        private void Session()
        {
            TcpClient tcp = new TcpClient();
            try
            {
                tcp.Connect(Host, Port);
                tcp.NoDelay = true;
                NetworkStream stream = tcp.GetStream();
                readerDone = false;
                connected = true;
                MelonLogger.Msg("connected to the bridge");
                Thread reader = new Thread(delegate () { ReadLoop(stream); });
                reader.IsBackground = true;
                reader.Start();
                lock (gate) { if (title == null) title = lastTitle; }
                while (running && !readerDone)
                {
                    byte[] jpg;
                    string text;
                    lock (gate)
                    {
                        if (frame == null && title == null) Monitor.Wait(gate, 250);
                        jpg = frame; frame = null;
                        text = title; title = null;
                    }
                    if (text != null) Send(stream, TypeTitle, Encoding.UTF8.GetBytes(text));
                    if (jpg != null) Send(stream, TypeImage, jpg);
                }
            }
            finally
            {
                tcp.Close();
            }
        }

        private static void Send(NetworkStream stream, int type, byte[] payload)
        {
            int length = payload.Length + 1;
            byte[] message = new byte[5 + payload.Length];
            message[0] = (byte)(length >> 24);
            message[1] = (byte)(length >> 16);
            message[2] = (byte)(length >> 8);
            message[3] = (byte)length;
            message[4] = (byte)type;
            Buffer.BlockCopy(payload, 0, message, 5, payload.Length);
            stream.Write(message, 0, message.Length);
        }

        private void ReadLoop(NetworkStream stream)
        {
            try
            {
                byte[] head = new byte[5];
                while (running)
                {
                    ReadExact(stream, head, 5);
                    int length = (head[0] << 24) | (head[1] << 16) | (head[2] << 8) | head[3];
                    if (length < 1 || length > (1 << 20)) break;
                    byte[] payload = new byte[length - 1];
                    ReadExact(stream, payload, payload.Length);
                    if (head[4] == TypeCommand)
                    {
                        lock (gate) commands.Enqueue(Encoding.UTF8.GetString(payload));
                    }
                }
            }
            catch (Exception)
            {
            }
            readerDone = true;
            lock (gate) Monitor.PulseAll(gate);
        }

        private static void ReadExact(NetworkStream stream, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                if (read <= 0) throw new System.IO.EndOfStreamException();
                offset += read;
            }
        }
    }
}
