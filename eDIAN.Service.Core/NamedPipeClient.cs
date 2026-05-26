using System;
using System.IO.Pipes;
using System.Threading.Tasks;

namespace eDIAN.Service.Core
{
    public class NamedPipeClient : NamedPipeBase<NamedPipeClientStream>
    {
        public event EventHandler ConnectedToServer;
        public event EventHandler ClientStarted;

        public NamedPipeClient(String name) : base(name)
        {

        }

        private void OnConnectedToServer()
        {
            ConnectedToServer?.Invoke(this, EventArgs.Empty);
        }

        private void OnClientStarted()
        {
            ClientStarted?.Invoke(this, EventArgs.Empty);
        }

        public async Task Connect(int timeout = 2000)
        {
            if (_name == null) 
            {
                return;
            }

            Initialize(new NamedPipeClientStream(".", _name, PipeDirection.InOut, PipeOptions.Asynchronous));

            if (Pipe == null)
            {
                return;
            }

            try
            {
                OnClientStarted();

                await Pipe.ConnectAsync(timeout);
                
                Pipe.ReadMode = PipeTransmissionMode.Message;

                OnConnectedToServer();

                await StartReading();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        public override void Dispose()
        {
            Pipe?.Dispose();
        }
    }
}
