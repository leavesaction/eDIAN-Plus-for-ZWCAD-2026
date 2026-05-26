using System;
using System.IO.Pipes;
using System.Threading.Tasks;

namespace eDIAN.Service.Core
{
    public abstract class NamedPipeBase<T> : IDisposable where T : PipeStream
    {
        public event EventHandler Disconnected;
        public event EventHandler<MessageReceivedEventArgs> MessageReceived;

        protected readonly String _name;
        protected T Pipe;
        private NamedPipeStream _stream;

        public NamedPipeBase(String pipeName)
        {
            _name = pipeName;
        }

        private void OnDisconnected()
        {
            Disconnected?.Invoke(this, EventArgs.Empty);
        }

        private void OnMessageReceived(String message)
        {
            MessageReceived?.Invoke(this, new MessageReceivedEventArgs(message));
        }

        protected void Initialize(T pipeStream)
        {
            Pipe = pipeStream;
            _stream = new NamedPipeStream(pipeStream);
        }

        protected async Task StartReading()
        {
            if (_stream == null)
            { 
                return;
            }

            await Task.Factory.StartNew(async () =>
            {
                try
                {
                    while (true)
                    {
                        OnMessageReceived(await _stream.ReadString());
                    }
                }
                catch (InvalidOperationException)
                {
                    OnDisconnected();
                    Dispose();
                }
            });
        }

        public async Task Send(String message)
        {
            if (_stream == null)
            {
                return;
            }

            await _stream.WriteString(message);
        }

        public abstract void Dispose();
    }
}
