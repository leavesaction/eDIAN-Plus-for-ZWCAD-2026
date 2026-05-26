using System;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;

namespace eDIAN.Service.Core
{
    public class NamedPipeStream
    {
        private PipeStream ioStream;

        public NamedPipeStream(PipeStream ioStream)
        {
            this.ioStream = ioStream;
        }

        public async Task<String> ReadString()
        {
            byte[] messageBytes = new byte[256];
            StringBuilder message = new StringBuilder();
            if (ioStream.CanRead)
            {
                // loop until the entire message is read
                do
                {
                    int bytesRead = await ioStream.ReadAsync(messageBytes, 0, messageBytes.Length);

                    // got bytes from the stream so add them to the message
                    if (bytesRead > 0)
                    {
                        message.Append(Encoding.Unicode.GetString(messageBytes, 0, bytesRead));

                        Array.Clear(messageBytes, 0, messageBytes.Length);
                    }
                    else
                    {
                        throw new InvalidOperationException("disconnected.");
                    }
                }
                while (!ioStream.IsMessageComplete);
            }
            return message.ToString();
        }

        public async Task WriteString(String message)
        {
            Byte[] messageBytes = Encoding.Unicode.GetBytes(message);

            if (ioStream.CanWrite)
            {
                await ioStream.WriteAsync(messageBytes, 0, messageBytes.Length);
                await ioStream.FlushAsync();

                ioStream.WaitForPipeDrain();
            }
        }
    }
}
