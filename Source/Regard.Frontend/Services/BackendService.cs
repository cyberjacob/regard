﻿using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using Regard.Api;
using Regard.Common.API;
using Regard.Common.API.Auth;
using Regard.Common.API.Response;
using Regard.Common.API.Subscriptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace Regard.Services
{
    public class BackendService
    {
        private readonly BackendHttpClient client;
        private readonly ILocalStorageService localStorage;

        class Empty { }

        private static readonly Empty EmptyRequest = new Empty();

        public Uri BaseUrl => client.BaseAddress;

        public BackendService(BackendHttpClient client, ILocalStorageService localStorage)
        {
            this.client = client;
            this.localStorage = localStorage;
        }

        private async Task UpdateAuthorization()
        {
            var token = await localStorage.GetItemAsync<string>(AuthenticationService.StorageAuthTokenKey);
            client.DefaultRequestHeaders.Authorization = string.IsNullOrWhiteSpace(token) ? null : new System.Net.Http.Headers.AuthenticationHeaderValue("bearer", token);
        }

        private Task<ApiResponse<TResponse>> Get<TResponse>(string uri)
        {
            return Get<Empty, TResponse>(uri, null);
        }

        private async Task<ApiResponse<TResponse>> Get<TRequest, TResponse>(string uri, TRequest data)
        {
            await UpdateAuthorization();

            uri = GenerateQueryString(uri, data);
            var dataResponse = await client.GetFromJsonAsync<ApiResponse<TResponse>>(uri);
            LogDebugMessage(dataResponse);

            return dataResponse;
        }

        private async Task<(ApiResponse, HttpResponseMessage)> Post<TRequest>(string uri, TRequest data)
        {
            await UpdateAuthorization();
            var response = await client.PostAsJsonAsync(uri, data);
            var dataResponse = await response.Content.ReadFromJsonAsync<ApiResponse>();
            LogDebugMessage(dataResponse);

            return (dataResponse, response);
        }

        private async Task<(ApiResponse<TResponse>, HttpResponseMessage)> Post<TRequest, TResponse>(string uri, TRequest data)
        {
            await UpdateAuthorization();
            var response = await client.PostAsJsonAsync(uri, data);
            var dataResponse = await response.Content.ReadFromJsonAsync<ApiResponse<TResponse>>();
            LogDebugMessage(dataResponse);

            return (dataResponse, response);
        }

        private static void LogDebugMessage(ApiResponse response)
        {
            if (!string.IsNullOrEmpty(response.DebugMessage))
            {
                Console.Error.WriteLine("Message from backend: ");
                Console.Error.WriteLine(response.DebugMessage);
            }
        }

        private static string GenerateQueryString<TRequest>(string uri, TRequest data)
        {
            if (data != null)
            {
                var values = new Dictionary<string, string>();
                foreach (var property in data.GetType().GetProperties())
                {
                    var value = property.GetValue(data, null);
                    if (value != null)
                        values.Add(property.Name, value.ToString());
                }

                uri = QueryHelpers.AddQueryString(uri, values);
            }

            return uri;
        }

        #region Setup

        public Task<ApiResponse<ServerStatusResponse>> SetupServerStatus() 
            => Get<ServerStatusResponse>("api/setup/server_status");

        public Task<(ApiResponse, HttpResponseMessage)> SetupInitialize()
            => Post("api/setup/initialize", EmptyRequest);

        #endregion

        #region Auth

        public Task<(ApiResponse<AuthResponse>, HttpResponseMessage)> AuthRegister(UserRegisterRequest data)
            => Post<UserRegisterRequest, AuthResponse>("api/auth/register", data);

        public Task<(ApiResponse<AuthResponse>, HttpResponseMessage)> AuthLogin(UserLoginRequest data)
            => Post<UserLoginRequest, AuthResponse>("api/auth/login", data);

        public Task<(ApiResponse<AuthResponse>, HttpResponseMessage)> AuthPromote(UserPromoteRequest data)
            => Post<UserPromoteRequest, AuthResponse>("api/auth/promote", data);

        public Task<ApiResponse<MeResponse>> AuthMe()
            => Get<MeResponse>("api/auth/me");

        #endregion

        #region Subscriptions

        public Task<(ApiResponse<SubscriptionValidateResponse>, HttpResponseMessage)> SubscriptionValidateUrl(SubscriptionValidateRequest data)
            => Post<SubscriptionValidateRequest, SubscriptionValidateResponse>("api/subscription/validate", data);

        public Task<(ApiResponse, HttpResponseMessage)> SubscriptionCreate(SubscriptionCreateRequest data)
            => Post("api/subscription/create", data);

        public Task<(ApiResponse, HttpResponseMessage)> SubscriptionCreateEmpty(SubscriptionCreateEmptyRequest data)
            => Post("api/subscription/create_empty", data);

        public Task<(ApiResponse<SubscriptionListResponse>, HttpResponseMessage)> SubscriptionList(SubscriptionListRequest data)
            => Post<SubscriptionListRequest, SubscriptionListResponse>("api/subscription/list", data);

        public Task<(ApiResponse, HttpResponseMessage)> SubscriptionDelete(SubscriptionDeleteRequest data)
            => Post("api/subscription/delete", data);

        public Task<(ApiResponse, HttpResponseMessage)> SubscriptionSynchronize(SubscriptionSynchronizeRequest data)
            => Post("api/subscription/synchronize", data);

        public Task<(ApiResponse, HttpResponseMessage)> SubscriptionSynchronizeAll()
            => Post("api/subscription/synchronize_all", EmptyRequest);

        public Task<(ApiResponse, HttpResponseMessage)> SubscriptionEdit(SubscriptionEditRequest data)
            => Post("api/subscription/edit", data);

        #endregion

        #region Subscription folders

        public Task<(ApiResponse, HttpResponseMessage)> SubscriptionFolderCreate(SubscriptionFolderCreateRequest data)
            => Post("api/subscriptionfolder/create", data);

        public Task<(ApiResponse<SubscriptionFolderListResponse>, HttpResponseMessage)> SubscriptionFolderList(SubscriptionFolderListRequest data)
            => Post<SubscriptionFolderListRequest, SubscriptionFolderListResponse>("api/subscriptionfolder/list", data);

        public Task<(ApiResponse, HttpResponseMessage)> SubscriptionFolderDelete(SubscriptionFolderDeleteRequest data)
            => Post("api/subscriptionfolder/delete", data);

        public Task<(ApiResponse, HttpResponseMessage)> SubscriptionFolderSynchronize(SubscriptionFolderSynchronizeRequest data)
            => Post("api/subscriptionfolder/synchronize", data);

        public Task<(ApiResponse, HttpResponseMessage)> SubscriptionFolderEdit(SubscriptionFolderEditRequest data)
            => Post("api/subscriptionfolder/edit", data);

        #endregion

        #region Videos

        public Task<(ApiResponse<VideoListResponse>, HttpResponseMessage)> VideoList(VideoListRequest data)
            => Post<VideoListRequest, VideoListResponse>("api/video/list", data);

        public Task<(ApiResponse, HttpResponseMessage)> VideoDownload(VideoDownloadRequest data)
            => Post("api/video/download", data);

        public Task<(ApiResponse, HttpResponseMessage)> VideoDeleteFiles(VideoDeleteFilesRequest data)
            => Post("api/video/delete_files", data);

        public Task<(ApiResponse, HttpResponseMessage)> VideoMarkWatched(VideoMarkWatchedRequest data)
            => Post("api/video/mark_watched", data);

        public Task<(ApiResponse, HttpResponseMessage)> VideoMarkNotWatched(VideoMarkNotWatchedRequest data)
            => Post("api/video/mark_not_watched", data);

        public Task<(ApiResponse, HttpResponseMessage)> VideoAdd(VideoAddRequest data)
            => Post("api/video/add", data);

        public Task<(ApiResponse, HttpResponseMessage)> VideoValidate(VideoValidateRequest data)
            => Post("api/video/validate", data);

        public async Task<Uri> VideoViewUrl(int videoId)
        {
            var token = await localStorage.GetItemAsync<string>(AuthenticationService.StorageAuthTokenKey);
            return new Uri(BaseUrl, $"/api/video/view?v={videoId}&access_token={token}");
        }

        #endregion
    }
}
