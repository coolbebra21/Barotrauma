using Barotrauma.IO;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    static partial class GameAnalyticsManager
    {
        static partial void CreateConsentPrompt()
        {
            SetConsent(Consent.Unknown);
        }
    }
}