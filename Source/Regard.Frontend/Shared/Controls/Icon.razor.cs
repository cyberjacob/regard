﻿using Microsoft.AspNetCore.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Regard.Frontend.Shared.Controls
{
    public enum Icons
    {
        Close,
        Add,
        Subscription,
        SubscriptionNew,
        Folder,
        FolderNew,
        Import,
        NotificationsFull,
        NotificationsEmpty,
        Profile,
        Settings,
        Menu,
        ChevronDown,
        Sort,
        Watched,
        NotWatched,
        Downloaded,
        NotDownloaded,
        Refresh,
        Synchronize,
        Checkmark,
        Filter,
        ShowWatched,
        HideWatched,
    }

    public partial class Icon
    {
        private static Dictionary<Icons, string> mapping = new Dictionary<Icons, string>()
        {
            { Icons.Close, "close" },
            { Icons.Add, "add" },
            { Icons.Subscription, "subject" },
            { Icons.SubscriptionNew, "playlist_add" },
            { Icons.Folder, "folder" },
            { Icons.FolderNew, "create_new_folder" },
            { Icons.Import, "publish" },
            { Icons.NotificationsFull, "notifications" },
            { Icons.NotificationsEmpty, "notifications_none" },
            { Icons.Profile, "person" },
            { Icons.Settings, "settings" },
            { Icons.Menu, "more_vert" },
            { Icons.ChevronDown, "expand_more" },
            { Icons.Sort, "sort" },
            { Icons.Watched, "done" },
            { Icons.NotWatched, "visibility_off" },
            { Icons.Downloaded, "cloud_download" },
            { Icons.NotDownloaded, "cloud_off" },
            { Icons.Refresh, "refresh" },
            { Icons.Synchronize, "cached" },
            { Icons.Checkmark, "done" },
            { Icons.Filter, "filter_alt" },
            { Icons.ShowWatched, "visibility" },
            { Icons.HideWatched, "visibility_off" },
        };

        [Parameter(CaptureUnmatchedValues = true)]
        public IReadOnlyDictionary<string, object> AdditionalAttributes { get; set; }

        [Parameter]
        public string Class { get; set; }

        [Parameter]
        public Icons Glyph { get; set; }

        private string Map(Icons icon)
        {
            if (mapping.TryGetValue(icon, out string value))
                return value;

            throw new NotImplementedException();
        }
    }
}
