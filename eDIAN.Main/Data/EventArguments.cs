using System;
using System.Collections.Generic;
using ZwSoft.ZwCAD.ApplicationServices;

namespace eDIAN.Main.Data
{
    public class DocumentEventArgs : EventArgs
    {
        public DocumentEventArgs(Document document)
        {
            this.document = document;
            this.isModifiedFile = false;
        }

        public DocumentEventArgs(Document document, bool isModifiedFile)
        {
            this.document = document;
            this.isModifiedFile = isModifiedFile;
        }

        public Document document { get; }
        public bool isModifiedFile { get; }
    }

    public class ProtectedDocumentEventArgs : EventArgs
    {
        public ProtectedDocumentEventArgs(ProtectedDocument protectedDocument)
        {
            this.protectedDocument = protectedDocument;
        }

        public ProtectedDocumentEventArgs(int selectedIndex)
        {
            this.selectedIndex = selectedIndex;
        }


        public ProtectedDocument protectedDocument { get; }

        public int selectedIndex { get; set; }
    }


    public class MessageEventArgs : EventArgs
    {

        public String message { get; }

        public String messageKind { get; set; }

        public MessageEventArgs(String message)
        {
            this.message = message;
        }

        public MessageEventArgs(String message, String messageKind)
        {
            this.message = message;

            this.messageKind = messageKind;
        }
    }

    public class AuthenticationEventArgs : EventArgs
    {
        public AuthenticationEventArgs(List<SensitivityLabelsOption> sensitivityLabels)
        {
            this.sensitivityLabels = sensitivityLabels;
        }

        public List<SensitivityLabelsOption> sensitivityLabels { get; }
    }

}
