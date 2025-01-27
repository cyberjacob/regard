﻿using Microsoft.EntityFrameworkCore.Internal;
using MoreLinq;
using Regard.Backend.Common.Providers;
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
    public class YouTubeAPIProvider : ISubscriptionProvider, IVideoProvider
    {
        public string Id => "YtAPI";

        public string Name => "YouTube API";

        public bool IsInitialized => (configuration.ApiKey != null);

        public Type ConfigurationType => typeof(YouTubeAPIConfiguration);

        private readonly YouTubeAPIConfiguration configuration = new YouTubeAPIConfiguration();

        public Task Configure(object config)
        {
            if (config is YouTubeAPIConfiguration ytapiConfig)
            {
                if (ytapiConfig.ApiKey == null)
                    throw new ArgumentException("API key is required!");

                this.configuration.ApiKey = ytapiConfig.ApiKey;
            }
            else
            {
                throw new ArgumentException("Provider must be set up!");
            }

            return Task.CompletedTask;
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

        public Task<bool> CanHandleVideo(Video video)
        {
            if (video.VideoProviderId == Id)
                return Task.FromResult(true);

            try
            {
                var parseResult = YouTubeUrlHelper.ParseUrl(new Uri(video.OriginalUrl));
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
                SubscriptionProviderId = Id,
                OriginalUrl = uri.ToString()
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
                sub.ThumbnailPath = channel.Snippet.Thumbnails.Maxres?.Url 
                    ?? channel.Snippet.Thumbnails.High?.Url
                    ?? channel.Snippet.Thumbnails.Standard?.Url
                    ?? channel.Snippet.Thumbnails.Medium?.Url
                    ?? channel.Snippet.Thumbnails.Default__?.Url; 
                sub.ProviderData = channel.ContentDetails.RelatedPlaylists.Uploads;
            }

            return sub;
        }
        /*
         x.Snippet.Thumbnails.Maxres?.Url 
                    ?? x.Snippet.Thumbnails.High?.Url
                    ?? x.Snippet.Thumbnails.Standard?.Url
                    ?? x.Snippet.Thumbnails.Medium?.Url
                    ?? x.Snippet.Thumbnails.Default__?.Url
         */
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
                            SubscriptionProviderId = x.Snippet.ResourceId.VideoId,
                            VideoProviderId = this.Id,
                            VideoId = x.Snippet.ResourceId.VideoId,
                            Name = x.Snippet.Title,
                            Description = x.Snippet.Description,
                            Subscription = subscription,
                            PlaylistIndex = i,
                            Published = x.Snippet.PublishedAt ?? DateTimeOffset.UtcNow,
                            LastUpdated = DateTime.Now,
                            ThumbnailPath = x.Snippet.Thumbnails.Maxres?.Url
                                ?? x.Snippet.Thumbnails.High?.Url
                                ?? x.Snippet.Thumbnails.Standard?.Url
                                ?? x.Snippet.Thumbnails.Medium?.Url
                                ?? x.Snippet.Thumbnails.Default__?.Url, 
                            UploaderName = x.Snippet.ChannelTitle,
                            OriginalUrl = $"https://www.youtube.com/watch?v={x.Snippet.ResourceId.VideoId}"
                        }); ;

                case YouTubeUrlType.Playlist:
                    return api.GetPlaylistVideos(subscription.ProviderData)
                        .Select((x, i) => new Video()
                        {
                            SubscriptionProviderId = x.Snippet.ResourceId.VideoId,
                            VideoProviderId = this.Id,
                            VideoId = x.Snippet.ResourceId.VideoId,
                            Name = x.Snippet.Title,
                            Description = x.Snippet.Description,
                            Subscription = subscription,
                            PlaylistIndex = Convert.ToInt32(x.Snippet.Position ?? i),
                            Published = x.Snippet.PublishedAt ?? DateTimeOffset.UtcNow,
                            LastUpdated = DateTime.Now,
                            ThumbnailPath = x.Snippet.Thumbnails.Maxres?.Url
                                ?? x.Snippet.Thumbnails.High?.Url
                                ?? x.Snippet.Thumbnails.Standard?.Url
                                ?? x.Snippet.Thumbnails.Medium?.Url
                                ?? x.Snippet.Thumbnails.Default__?.Url,
                            UploaderName = x.Snippet.ChannelTitle,
                            OriginalUrl = $"https://www.youtube.com/watch?v={x.Snippet.ResourceId.VideoId}"
                        });

                case YouTubeUrlType.Search:
                    return api.GetSearchResults(parseResult.Query, "video")
                        .Select((x, i) => new Video()
                        {
                            SubscriptionProviderId = x.Id.VideoId,
                            VideoProviderId = this.Id,
                            VideoId = x.Id.VideoId,
                            Name = x.Snippet.Title,
                            Description = x.Snippet.Description,
                            Subscription = subscription,
                            PlaylistIndex = i,
                            Published = x.Snippet.PublishedAt ?? DateTimeOffset.UtcNow,
                            LastUpdated = DateTime.Now,
                            ThumbnailPath = x.Snippet.Thumbnails.Maxres?.Url
                                ?? x.Snippet.Thumbnails.High?.Url
                                ?? x.Snippet.Thumbnails.Standard?.Url
                                ?? x.Snippet.Thumbnails.Medium?.Url
                                ?? x.Snippet.Thumbnails.Default__?.Url,
                            UploaderName = x.Snippet.ChannelTitle,
                            OriginalUrl = $"https://www.youtube.com/watch?v={x.Id.VideoId}"
                        });

                default:
                    throw new ArgumentException("Unsupported resource type!");
            }
        }

        public IAsyncEnumerable<Uri> FetchVideoUrls(Subscription subscription)
        {
            throw new InvalidOperationException("Operation not supported!");
        }

        public async Task UpdateMetadata(IEnumerable<Video> videos, bool updateMetadata, bool updateStatistics)
        {
            var api = new YouTubeAPIProxy(configuration.ApiKey);

            List<string> parts = new List<string>
            {
                "id,snippet"
            };
            if (updateMetadata)
                parts.Add("snippet");
            if (updateStatistics)
                parts.Add("statistics");

            foreach (var batch in videos.Batch(50))
            {
                var batchList = batch.ToList();
                int idx = 0;

                // Some videos might not have ANY metadata added, so first we should preprocess them
                for (int i = 0; i < batchList.Count; i++)
                {
                    var video = batchList[i];
                    if (video.VideoId == null)
                    {
                        var parseResult = YouTubeUrlHelper.ParseUrl(new Uri(video.OriginalUrl));
                        if (parseResult.Type == YouTubeUrlType.Video)
                        {
                            video.VideoId = parseResult.VideoId;
                            video.VideoProviderId = Id;
                        }
                        else
                        {
                            // remove from batch, video doesn't seem to be valid
                            // TODO: log
                            batchList.RemoveAt(i--);
                        }
                    }
                }


                await foreach (var ytVideo in api.GetVideos(batchList.Select(x => x.VideoId), parts))
                {
                    var video = batchList.First(x => x.VideoId == ytVideo.Id);

                    if (updateMetadata)
                    {
                        video.Name = ytVideo.Snippet.Title;
                        video.Description = ytVideo.Snippet.Description;
                        video.Published = ytVideo.Snippet.PublishedAt ?? DateTimeOffset.UtcNow;
                        video.LastUpdated = DateTime.Now;
                        video.ThumbnailPath = ytVideo.Snippet.Thumbnails.Maxres?.Url
                                ?? ytVideo.Snippet.Thumbnails.High?.Url
                                ?? ytVideo.Snippet.Thumbnails.Standard?.Url
                                ?? ytVideo.Snippet.Thumbnails.Medium?.Url
                                ?? ytVideo.Snippet.Thumbnails.Default__?.Url;
                        video.UploaderName = ytVideo.Snippet.ChannelTitle;
                    }
                    if (updateStatistics)
                    {
                        if (ytVideo.Statistics.ViewCount.HasValue)
                            video.Views = ytVideo.Statistics.ViewCount.Value;

                        if (ytVideo.Statistics.LikeCount.HasValue && ytVideo.Statistics.DislikeCount.HasValue)
                        {
                            ulong total = ytVideo.Statistics.LikeCount.Value + ytVideo.Statistics.DislikeCount.Value;
                            if (total > 0)
                                video.Rating = Convert.ToSingle(ytVideo.Statistics.LikeCount.Value) / Convert.ToSingle(total);
                        }
                    }
                    ++idx;
                }
            }
        }
    }
}
