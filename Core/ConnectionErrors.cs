using System;
using System.IO;
using System.Net.Sockets;

namespace DLC_PRO.Core;

public static class ConnectionErrors
{
    public static string Describe(Exception error, bool usb, string endpoint)
    {
        if (error is AggregateException aggregate) error = aggregate.GetBaseException();
        if (usb)
        {
            if (error is FileNotFoundException || error is DirectoryNotFoundException)
                return $"USB 포트를 찾을 수 없습니다: {endpoint}\nUSB 케이블과 COM 포트 번호를 확인해 주세요.";
            if (error is UnauthorizedAccessException)
                return $"USB 포트를 열 수 없습니다: {endpoint}\nTOPAS 등 다른 프로그램이 사용 중인지 확인해 주세요.";
            return $"USB 장비에 연결할 수 없습니다: {endpoint}\n케이블, 포트 번호와 장비 전원을 확인해 주세요.\n\n{error.Message}";
        }
        if (error is TimeoutException || error is SocketException)
            return $"IP 주소의 장비를 찾을 수 없거나 응답이 없습니다: {endpoint}\nIP 주소, 장비 전원, 네트워크와 방화벽을 확인해 주세요.";
        return $"장비에 연결할 수 없습니다: {endpoint}\n\n{error.Message}";
    }
}
