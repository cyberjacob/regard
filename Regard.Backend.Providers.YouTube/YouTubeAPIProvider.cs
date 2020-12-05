﻿using Microsoft.EntityFrameworkCore.Internal;
using MoreLinq;
using Regard.Backend.Model;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Subscription = Regard.Backend.Model.Subscription;
using YtChannel = Google.Apis.YouTube.v3.Data.Channel;

namespace Regard.Backend.Providers.YouTube
{
    public class YouTubeAPIProvider : ICompleteProvider
    {
        public string ProviderId => "YtAPI";

        public string Name => "YouTube API";

        public bool IsInitialized => (configuration.ApiKey != null);

        public Type ConfigurationType => typeof(YouTubeAPIConfiguration);

        private readonly YouTubeAPIConfiguration configuration = new YouTubeAPIConfiguration();

        public void Configure(object config)
        {
            if (config is YouTubeAPIConfiguration ytapiConfig)
            {
                if (ytapiConfig.ApiKey == null)
                    throw new ArgumentException("API key is required!");

                this.configuration.ApiKey = ytapiConfig.ApiKey;
            }

            throw new ArgumentException("Wrong data type");
        }

        public void Unconfigure()
        {
            this.configuration.ApiKey = null;
        }

        public Task<bool> CanHandleSubscriptionUrl(Uri uri)
        {
            try
            {
                var parseResult = YouTubeUrlHelper.ParseUrl(uri);
                switch (parseResult.Type)
                {
                    case YouTubeUrlType.Channel:
                    case YouTubeUrlType.ChannelCustom:
                    case YouTubeUrlType.Playlist:
                    case YouTubeUrlType.Search:
                    case YouTubeUrlType.User:
                        return Task.FromResult(true);
                    default:
                        return Task.FromResult(false);
                }
            }
            catch (ArgumentException)
            {
                return Task.FromResult(false);
            }
        }

        public Task<bool> CanHandleVideoUrl(Uri uri)
        {
            try
            {
                var parseResult = YouTubeUrlHelper.ParseUrl(uri);
                return Task.FromResult(parseResult.Type == YouTubeUrlType.Video);
            }
            catch (ArgumentException)
            {
                return Task.FromResult(false);
            }
        }

        public async Task<Subscription> CreateSubscription(Uri uri)
        {
            var parseResult = YouTubeUrlHelper.ParseUrl(uri);
            var api = new YouTubeAPIProxy(configuration.ApiKey);

            Subscription sub = new Subscription
            {
                SubscriptionId = uri.AbsoluteUri,
                SubscriptionProviderId = ProviderId,
            };

            YtChannel channel = null;

            switch (parseResult.Type)
            {
                case YouTubeUrlType.Channel:
                    channel = await api.FetchChannel(parseResult.ChannelId);
                    break;
                case YouTubeUrlType.ChannelCustom:
                    var channelSearch = await api.FetchChannelByCustomId(parseResult.ChannelCustomId);
                    channel = await api.FetchChannel(channelSearch.Id.ChannelId);
                    break;
                case YouTubeUrlType.User:
                    channel = await api.FetchChannelByUserId(parseResult.UserId);
                    break;
                case YouTubeUrlType.Playlist:
                    {
                        var playlist = await api.FetchPlaylist(parseResult.ListId);
                        sub.Name = playlist.Snippet.Title;
                        sub.Description = playlist.Snippet.Description;
                        sub.ThumbnailPath = playlist.Snippet.Thumbnails.Maxres.Url;
                        sub.ProviderData = playlist.Id;
                    }
                    break;
                case YouTubeUrlType.Search:
                    {
                        sub.Name = uri.Query;
                        sub.Description = "Automatic subscription generated from a search query!";
                    }
                    break;
                default:
                    throw new Exception("Unsupported resource type!");
            }

            if (channel != null)
            {
                sub.Name = channel.Snippet.Title;
                sub.Description = channel.Snippet.Description;
                sub.ThumbnailPath = channel.Snippet.Thumbnails.Maxres.Url;
                sub.ProviderData = channel.ContentDetails.RelatedPlaylists.Uploads;
            }

            return sub;
        }

        public IAsyncEnumerable<Video> FetchVideos(Subscription subscription)
        {
            var parseResult = YouTubeUrlHelper.ParseUrl(new Uri(subscription.SubscriptionId));
            var api = new YouTubeAPIProxy(configuration.ApiKey);

            switch (parseResult.Type)
            {
                case YouTubeUrlType.Channel:
                case YouTubeUrlType.ChannelCustom:
                case YouTubeUrlType.User:
                    return api.GetPlaylistVideos(subscription.ProviderData)
                        .Reverse()
                        .Select((x, i) => new Video()
                        {
                            ProviderId = this.ProviderId,
                            VideoId = x.Snippet.ResourceId.VideoId,
                            Name = x.Snippet.Title,
                            Description = x.Snippet.Description,
                            Subscription = subscription,
                            PlaylistIndex = i,
                            Published = DateTime.Parse(x.Snippet.PublishedAt, styles: DateTimeStyles.RoundtripKind),
                            LastUpdated = DateTime.Now,
                            ThumbnailPath = x.Snippet.Thumbnails.Maxres.Url,
                            UploaderName = x.Snippet.ChannelTitle
                        });

                case YouTubeUrlType.Playlist:
                    return api.GetPlaylistVideos(subscription.ProviderData)
                        .Select((x, i) => new Video()
                        {
                            ProviderId = this.ProviderId,
                            VideoId = x.Snippet.ResourceId.VideoId,
                            Name = x.Snippet.Title,
                            Description = x.Snippet.Description,
                            Subscription = subscription,
                            PlaylistIndex = Convert.ToInt32(x.Snippet.Position ?? i),
                            Published = DateTime.Parse(x.Snippet.PublishedAt, styles: DateTimeStyles.RoundtripKind),
                            LastUpdated = DateTime.Now,
                            ThumbnailPath = x.Snippet.Thumbnails.Maxres.Url,
                            UploaderName = x.Snippet.ChannelTitle
                        });

                case YouTubeUrlType.Search:
                    return api.GetSearchResults(parseResult.Query, "video")
                        .Select((x, i) => new Video()
                        {
                            ProviderId = this.ProviderId,
                            VideoId = x.Id.VideoId,
                            Name = x.Snippet.Title,
                            Description = x.Snippet.Description,
                            Subscription = subscription,
                            PlaylistIndex = i,
                            Published = DateTime.Parse(x.Snippet.PublishedAt, styles: DateTimeStyles.RoundtripKind),
                            LastUpdated = DateTime.Now,
                            ThumbnailPath = x.Snippet.Thumbnails.Maxres.Url,
                            UploaderName = x.Snippet.ChannelTitle
                        });

                default:
                    throw new ArgumentException("Unsupported resource type!");
            }
        }

        public async IAsyncEnumerable<Video> FetchVideos(IEnumerable<Uri> urls)
        {
            var api = new YouTubeAPIProxy(configuration.ApiKey);

            var videoIdBatches = urls.Select(YouTubeUrlHelper.ParseUrl)
                .Where(x => x.Type == YouTubeUrlType.Video)
                .Select(x => x.VideoId)
                .Batch(50);

            foreach (var batch in videoIdBatches)
            {
                await foreach (var x in api.GetVideos(batch, "id", "snippet"))
                {
                    yield return new Video()
                    {
                        ProviderId = this.ProviderId,
                        VideoId = x.Id,
                        Name = x.Snippet.Title,
                        Description = x.Snippet.Description,
                        Published = DateTime.Parse(x.Snippet.PublishedAt, styles: DateTimeStyles.RoundtripKind),
                        LastUpdated = DateTime.Now,
                        ThumbnailPath = x.Snippet.Thumbnails.Maxres.Url,
                        UploaderName = x.Snippet.ChannelTitle
                    };
                }
            }
        }

        public async Task UpdateMetadata(IEnumerable<Video> videos, bool updateMetadata, bool updateStatistics)
        {
            var api = new YouTubeAPIProxy(configuration.ApiKey);

            List<string> parts = new List<string>
            {
                "id"
            };
            if (updateMetadata)
                parts.Add("snippet");
            if (updateStatistics)
                parts.Add("statistics");

            foreach (var batch in videos.Batch(50))
            {
                var batchArray = batch.ToArray();
                int idx = 0;

                await foreach (var ytVideo in api.GetVideos(batchArray.Select(x => x.VideoId), parts))
                {
                    var video = batchArray.First(x => x.VideoId == ytVideo.Id);

                    if (updateMetadata)
                    {
                        video.Name = ytVideo.Snippet.Title;
                        video.Description = ytVideo.Snippet.Description;
                        video.Published = DateTime.Parse(ytVideo.Snippet.PublishedAt, styles: DateTimeStyles.RoundtripKind);
                        video.LastUpdated = DateTime.Now;
                        video.ThumbnailPath = ytVideo.Snippet.Thumbnails.Maxres.Url;
                        video.UploaderName = ytVideo.Snippet.ChannelTitle;
                    }
                    if (updateStatistics)
                    {
                        if (ytVideo.Statistics.ViewCount.HasValue)
                            video.Views = Convert.ToInt32(ytVideo.Statistics.ViewCount.Value);

                        if (ytVideo.Statistics.LikeCount.HasValue && ytVideo.Statistics.DislikeCount.HasValue)
                            video.Rating = Convert.ToSingle(ytVideo.Statistics.LikeCount.Value) / Convert.ToSingle(ytVideo.Statistics.DislikeCount.Value);
                    }
                    ++idx;
                }
            }
        }
    }
}
