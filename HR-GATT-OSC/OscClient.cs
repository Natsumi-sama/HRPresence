using System.Net;
using System.Net.Sockets;
using OscCore;
using OscQueryLibrary;

namespace HR_GATT_OSC;

public class OscServer
{
    private static UdpClient? _udp;
    private static IPEndPoint _sender = new(IPAddress.Any, 0);
    private static Thread _listenerThread;

    public void ListenForOscMessages(int oscPort)
    {
        _udp?.Dispose();
        _udp = new UdpClient(oscPort);
        _listenerThread = new Thread(() =>
        {
            while (true)
            {
                var data = _udp.Receive(ref _sender);
                ReceiveOscMessage(data);
            }
        });
        _listenerThread.Start();
    }

    private static void ReceiveOscMessage(byte[] data)
    {
        try
        {
            var msg = new OscMessageRaw(new ArraySegment<byte>(data));
            if (msg.Address == "/avatar/change")
            {
                Console.WriteLine("Avatar change");
                OscQueryServer.GetParameters().GetAwaiter();
            }
            // Console.WriteLine(msg.Address);
        }
        catch (Exception ex)
        {
            // VRChat moment
        }
    }
}