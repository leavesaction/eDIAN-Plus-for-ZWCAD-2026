using System;
using System.IO.Pipes;
using System.Threading.Tasks;

namespace eDIAN.Service.Core
{
    public class NamedPipeServer : NamedPipeBase<NamedPipeServerStream>
    {
        public event EventHandler ServerStarted;
        public event EventHandler ClientConnected;

        public NamedPipeServer(String name) : base(name)
        {
        }

        private void OnClientConnected()
        {
            ClientConnected?.Invoke(this, EventArgs.Empty);
        }

        private void OnServerStarted()
        {
            ServerStarted?.Invoke(this, EventArgs.Empty);
        }

        public async Task Start()
        {
            Initialize(new NamedPipeServerStream(_name ?? "", PipeDirection.InOut, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous));

            try
            {
                Pipe?.BeginWaitForConnection(WaitForConnectionCallBack, null);

                OnServerStarted();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        private void WaitForConnectionCallBack(IAsyncResult result)
        {
            Pipe?.EndWaitForConnection(result);
            OnClientConnected();

            StartReading().GetAwaiter().GetResult();
        }

        public override void Dispose()
        {
            Pipe?.Disconnect();
            Pipe?.Dispose();
        }
    }
}
