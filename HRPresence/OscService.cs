using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace HRPresence
{
    internal class OscService
    {
        public UdpClient udp;

        public void Initialize(IPAddress ip, int port)
        {
            udp = new UdpClient();
            udp.Connect(ip, port);
        }

        public bool Update(int heartrate, int rrInterval)
        {
            // Maps the heart rate from [0;255] to [-1;+1]
            var floatHR = (heartrate * 0.0078125f) - 1.0f;
            var data = new (string, object)[] {
                ("isHRConnected", Program.isHRConnected),
                ("HR"        , heartrate),
                ("onesHR"    , (heartrate      ) % 10),
                ("tensHR"    , (heartrate / 10 ) % 10),
                ("hundredsHR", (heartrate / 100) % 10),
                ("floatHR"   , floatHR),
                ("RRInterval", rrInterval)
            };

            try
            {
                foreach (var (path, value) in data)
                {
                    var bytes = new OscCore.OscMessage($"/avatar/parameters/{path}", value).ToByteArray();
                    udp.Send(bytes, bytes.Length);
                }
            }
            catch
            {
                return false;
            }
            return true;
        }

        public void Clear()
        {
            var data = new (string, object)[] {
                ("isHRConnected", false),
                ("HR"        , 0),
                ("onesHR"    , 0),
                ("tensHR"    , 0),
                ("hundredsHR", 0),
                ("floatHR"   , -1f),
                ("isHRBeat"  , false),
                ("RRInterval", 0)
            };
            try
            {
                foreach (var (path, value) in data)
                {
                    var bytes = new OscCore.OscMessage($"/avatar/parameters/{path}", value).ToByteArray();
                    udp.Send(bytes, bytes.Length);
                }
            }
            catch
            {
            }
        }

        public void SendBeat()
        {
            try
            {
                var bytes = new OscCore.OscMessage($"/avatar/parameters/isHRBeat", true).ToByteArray();
                udp.Send(bytes, bytes.Length);
                Thread.Sleep(100);
                var bytes1 = new OscCore.OscMessage($"/avatar/parameters/isHRBeat", false).ToByteArray();
                udp.Send(bytes1, bytes1.Length);
            }
            catch
            {
            }
        }
    }
}