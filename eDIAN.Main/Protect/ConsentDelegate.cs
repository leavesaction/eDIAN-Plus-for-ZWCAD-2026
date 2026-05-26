using Microsoft.InformationProtection;
using System;

namespace eDIAN.Main.Protect

{
    internal class ConsentDelegate : IConsentDelegate
    {
        public Consent GetUserConsent(String url)
        {
            return Consent.Accept;
        }
    }    
}
