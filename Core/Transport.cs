#nullable disable
using System;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace DLC_PRO.Core
{
    /// <summary>
    /// DLC pro와 바이트 스트림을 주고받는 통신 수단 (TCP 또는 USB 가상 COM 포트).
    /// </summary>
    public interface ITransport : IDisposable
    {
        bool IsOpen { get; }
        int ReadTimeoutMs { get; set; }
        /// <summary>지금 바로 읽을 수 있는 바이트 수.</summary>
        int BytesAvailable { get; }
        void Write(string text);
        /// <summary>데이터를 읽는다. 타임아웃이면 TimeoutException, 연결 종료면 IOException.</summary>
        int Read(byte[] buffer, int offset, int count);
        string Description { get; }
    }

    /// <summary>TCP/IP 전송 (명령 포트 1998, 모니터링 포트 1999).</summary>
    public sealed class TcpTransport : ITransport
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly string _desc;

        public TcpTransport(string host, int port, int connectTimeoutMs = 3000)
        {
            _client = new TcpClient();
            IAsyncResult ar = _client.BeginConnect(host, port, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(connectTimeoutMs))
            {
                try { _client.Close(); } catch { }
                throw new TimeoutException("연결 시간 초과: " + host + ":" + port);
            }
            try { _client.EndConnect(ar); }
            catch { try { _client.Close(); } catch { } throw; }
            // 매뉴얼 4.4.1: TCP_NODELAY 설정 권장
            _client.NoDelay = true;
            _stream = _client.GetStream();
            _stream.WriteTimeout = 3000;
            _desc = host + ":" + port;
            ReadTimeoutMs = 3000;
        }

        public bool IsOpen { get { return _client != null && _client.Connected; } }

        public int ReadTimeoutMs
        {
            get { return _stream.ReadTimeout; }
            set { _stream.ReadTimeout = value; }
        }

        public string Description { get { return "TCP " + _desc; } }

        public int BytesAvailable { get { return _client.Available; } }

        public void Write(string text)
        {
            byte[] b = Encoding.UTF8.GetBytes(text);
            _stream.Write(b, 0, b.Length);
            _stream.Flush();
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            try
            {
                int n = _stream.Read(buffer, offset, count);
                if (n == 0) throw new IOException("장비가 연결을 종료했습니다.");
                return n;
            }
            catch (IOException ex)
            {
                SocketException se = ex.InnerException as SocketException;
                if (se != null && se.SocketErrorCode == SocketError.TimedOut)
                    throw new TimeoutException("응답 시간 초과 (" + _desc + ")", ex);
                throw;
            }
        }

        public void Dispose()
        {
            try { _stream.Dispose(); } catch { }
            try { _client.Close(); } catch { }
        }
    }
}
