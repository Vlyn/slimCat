﻿#region Copyright

// --------------------------------------------------------------------------------------------------------------------
// <copyright file="TypeCommand.cs">
//     Copyright (c) 2013, Justin Kadrovach, All rights reserved.
//  
//     This source is subject to the Simplified BSD License.
//     Please see the License.txt file for more information.
//     All other rights reserved.
// 
//     THIS CODE AND INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY 
//     KIND, EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
//     IMPLIED WARRANTIES OF MERCHANTABILITY AND/OR FITNESS FOR A
//     PARTICULAR PURPOSE.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

#endregion

namespace slimCat.Models
{
    using Utilities;

    public class ChannelTypeChangedEventArgs : ChannelUpdateEventArgs
    {
        public bool IsOpen { get; set; }

        public override string ToString()
        {
            return "{0} is now {1}".FormatWith(GetChannelBbCode(), IsOpen ? "open" : "invite only");
        }
    }
}

namespace slimCat.Services
{
    #region Usings

    using System;
    using System.Collections.Generic;
    using Models;
    using Utilities;

    #endregion

    public partial class ServerCommandService
    {
        private void RoomTypeChangedCommand(IDictionary<string, object> command)
        {
            var channelId = command.Get(Constants.Arguments.Channel);
            var isPublic =
                (command.Get(Constants.Arguments.Message)).IndexOf("public", StringComparison.OrdinalIgnoreCase) !=
                -1;

            var channel = ChatModel.CurrentChannels.FirstByIdOrNull(channelId);

            if (channel == null)
                return; // can't change the settings of a room we don't know

            channel.Type = isPublic ? ChannelType.Private : ChannelType.InviteOnly;

            var updateArgs = new ChannelTypeChangedEventArgs
            {
                IsOpen = isPublic
            };


            Events.NewChannelUpdate(channel, updateArgs);
        }
    }
}