#nullable disable
using System;
using System.IO.Ports;
using System.Text;

namespace DLC_PRO.Core
{
    /// <summary>
    /// USB 연결 시 생성되는 가상 COM 포트 전송 (매뉴얼 4.3).
    /// 보레이트 등 설정은 장비가 무시하므로 기본값을 사용한다. 모니터링 라인은 지원되지 않는다.
    /// </summary>
    public sealed class SerialTransport : ITransport
    {
        private readonly SerialPort _port;

        public SerialTransport(string portName)
        {
            _port = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One);
            _port.Handshake = Handshake.None;
            _port.Encoding = Encoding.UTF8;
            _port.DtrEnable = true;
            _port.RtsEnable = true;
            _port.ReadTimeout = 3000;
            _port.WriteTimeout = 3000;
            try { _port.Open(); }
            catch { _port.Dispose(); throw; }
        }

        public bool IsOpen { get { return _port.IsOpen; } }

        public int ReadTimeoutMs
        {
            get { return _port.ReadTimeout; }
            set { _port.ReadTimeout = value; }
        }

        public string Description { get { return "USB " + _port.PortName; } }

        public int BytesAvailable { get { return _port.IsOpen ? _port.BytesToRead : 0; } }

        public void Write(string text)
        {
            byte[] b = Encoding.UTF8.GetBytes(text);
            _port.Write(b, 0, b.Length);
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            // SerialPort.Read는 타임아웃 시 TimeoutException을 던진다.
            return _port.Read(buffer, offset, count);
        }

        public void Dispose()
        {
            try { if (_port.IsOpen) _port.Close(); } catch { }
            _port.Dispose();
        }
    }
}
