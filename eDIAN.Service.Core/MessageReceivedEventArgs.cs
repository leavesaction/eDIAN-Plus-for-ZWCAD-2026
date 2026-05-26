using System;

namespace eDIAN.Service.Core
{
    public class MessageReceivedEventArgs : EventArgs
    {
        public String Message { get; }

        public MessageReceivedEventArgs(String message)
        {
            Message = message;
        }
    }
}
