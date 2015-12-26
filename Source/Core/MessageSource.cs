//-----------------------------------------------------------------------
// <copyright file="MessageSource.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;

namespace Core
{
    public static class MessageSource
    {
        private static readonly Dictionary<string, string> Messages;

        static MessageSource()
        {
            Messages = new Dictionary<string, string>
                           {
                               {"cargo.status.NOT_RECEIVED", "Not received"},
                               {"cargo.status.IN_PORT", "In port {0}"},
                               {"cargo.status.ONBOARD_CARRIER", "Onboard voyage {0}"},
                               {"cargo.status.CLAIMED", "Claimed"},
                               {"cargo.status.UNKNOWN", "Unknown"},
                               {"deliveryHistory.eventDescription.NOT_RECEIVED", "Cargo has not yet been received."},
                               {"deliveryHistory.eventDescription.LOAD", "Loaded onto voyage {0} in {1}, at {2}."},
                               {"deliveryHistory.eventDescription.UNLOAD", "Unloaded off voyage {0} in {1}, at {2}."},
                               {"deliveryHistory.eventDescription.RECEIVE", "Received in {0}, at {1}."},
                               {"deliveryHistory.eventDescription.CLAIM", "Claimed in {0}, at {1}."},
                               {"deliveryHistory.eventDescription.CUSTOMS", "Cleared customs in {0}, at {1}."},
                               {"cargo.unknown_id", "Unknown tracking id"}
                           };
        }

        public static string GetMessage(string key, object[] args, string defaultMsg)
        {
            string msgValue;
            Messages.TryGetValue(key, out msgValue);

            if (string.IsNullOrEmpty(msgValue))
            {
                return defaultMsg;
            }

            if (args == null)
            {
                return msgValue;
            }

            return string.Format(msgValue, args);
        }

        public static string GetMessage(string key, object[] args)
        {
            return string.Format(Messages[key], args);
        }

        public static string GetMessage(string key)
        {
            return string.Format(Messages[key]);
        }
    }
}
