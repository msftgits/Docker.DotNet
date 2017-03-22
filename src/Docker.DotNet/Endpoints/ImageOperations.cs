﻿using Docker.DotNet.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Docker.DotNet
{
    internal class ImageOperations : IImageOperations
    {
        internal static readonly ApiResponseErrorHandlingDelegate NoSuchImageHandler = (statusCode, responseBody) =>
        {
            if (statusCode == HttpStatusCode.NotFound)
            {
                throw new DockerImageNotFoundException(statusCode, responseBody);
            }
        };

        private const string RegistryAuthHeaderKey = "X-Registry-Auth";
        private const string TarContentType = "application/x-tar";
        private const string ImportFromBodySource = "-";

        private readonly DockerClient _client;

        internal ImageOperations(DockerClient client)
        {
            this._client = client;
        }

        public async Task<IList<ImagesListResponse>> ListImagesAsync(ImagesListParameters parameters, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            IQueryString queryParameters = new QueryString<ImagesListParameters>(parameters);
            var response = await this._client.MakeRequestAsync(this._client.NoErrorHandlers, HttpMethod.Get, "images/json", queryParameters, cancellationToken).ConfigureAwait(false);
            return this._client.JsonSerializer.DeserializeObject<ImagesListResponse[]>(response.Body);
        }

        public async Task<ImageInspectResponse> InspectImageAsync(string name, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentNullException(nameof(name));
            }

            var response = await this._client.MakeRequestAsync(new[] { NoSuchImageHandler }, HttpMethod.Get, $"images/{name}/json", cancellationToken).ConfigureAwait(false);
            return this._client.JsonSerializer.DeserializeObject<ImageInspectResponse>(response.Body);
        }

        public async Task<IList<ImageHistoryResponse>> GetImageHistoryAsync(string name, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentNullException(nameof(name));
            }

            var response = await this._client.MakeRequestAsync(new[] { NoSuchImageHandler }, HttpMethod.Get, $"images/{name}/history", cancellationToken).ConfigureAwait(false);
            return this._client.JsonSerializer.DeserializeObject<ImageHistoryResponse[]>(response.Body);
        }

        public Task TagImageAsync(string name, ImageTagParameters parameters, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentNullException(nameof(name));
            }

            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            IQueryString queryParameters = new QueryString<ImageTagParameters>(parameters);
            return this._client.MakeRequestAsync(new[] { NoSuchImageHandler }, HttpMethod.Post, $"images/{name}/tag", queryParameters, cancellationToken);
        }

        public async Task<IList<IDictionary<string, string>>> DeleteImageAsync(string name, ImageDeleteParameters parameters, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentNullException(nameof(name));
            }

            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            IQueryString queryParameters = new QueryString<ImageDeleteParameters>(parameters);
            var response = await this._client.MakeRequestAsync(new[] { NoSuchImageHandler }, HttpMethod.Delete, $"images/{name}", queryParameters, cancellationToken).ConfigureAwait(false);
            return this._client.JsonSerializer.DeserializeObject<Dictionary<string, string>[]>(response.Body);
        }

        public async Task<IList<ImageSearchResponse>> SearchImagesAsync(ImagesSearchParameters parameters, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            IQueryString queryParameters = new QueryString<ImagesSearchParameters>(parameters);
            var response = await this._client.MakeRequestAsync(this._client.NoErrorHandlers, HttpMethod.Get, "images/search", queryParameters, cancellationToken).ConfigureAwait(false);
            return this._client.JsonSerializer.DeserializeObject<ImageSearchResponse[]>(response.Body);
        }

        public Task<Stream> CreateImageAsync(ImagesCreateParameters parameters, AuthConfig authConfig)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            return PullImageAsync(new ImagesPullParameters() { All = false, Parent = parameters.Parent, RegistryAuth = parameters.RegistryAuth }, authConfig);
        }

        public Task CreateImageAsync(ImagesCreateParameters parameters, AuthConfig authConfig, IProgress<JSONMessage> progress, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            return PullImageAsync(new ImagesPullParameters() { All = false, Parent = parameters.Parent, RegistryAuth = parameters.RegistryAuth }, authConfig, progress, cancellationToken);
        }

        public Task<Stream> PullImageAsync(ImagesPullParameters parameters, AuthConfig authConfig)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            IQueryString queryParameters = new QueryString<ImagesPullParameters>(parameters);
            return this._client.MakeRequestForStreamAsync(this._client.NoErrorHandlers, HttpMethod.Post, "images/create", queryParameters, null, RegistryAuthHeaders(authConfig), CancellationToken.None);
        }

        public Task PullImageAsync(ImagesPullParameters parameters, AuthConfig authConfig, IProgress<JSONMessage> progress, CancellationToken cancellationToken = default(CancellationToken))
        {
            return StreamUtil.MonitorStreamForMessagesAsync(
                PullImageAsync(parameters, authConfig),
                this._client,
                cancellationToken,
                progress);
        }

        public Task<Stream> PushImageAsync(string name, ImagePushParameters parameters, AuthConfig authConfig)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentNullException(nameof(name));
            }

            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            IQueryString queryParameters = new QueryString<ImagePushParameters>(parameters);
            return this._client.MakeRequestForStreamAsync(this._client.NoErrorHandlers, HttpMethod.Post, $"images/{name}/push", queryParameters, null, RegistryAuthHeaders(authConfig), CancellationToken.None);
        }

        public Task PushImageAsync(string name, ImagePushParameters parameters, AuthConfig authConfig, IProgress<JSONMessage> progress, CancellationToken cancellationToken = default(CancellationToken))
        {
            return StreamUtil.MonitorStreamForMessagesAsync(
                PushImageAsync(name, parameters, authConfig),
                this._client,
                cancellationToken,
                progress);
        }

        public Task ImportImageAsync(ImagesImportParameters parameters, AuthConfig authConfig, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrEmpty(parameters.SourceName)
                || parameters.SourceName == ImportFromBodySource)
            {
                throw new ArgumentException("SourceName must be a URL where the image can be retrieved");
            }

            return this.ImportImageAsync(parameters, null, authConfig, cancellationToken);
        }

        public Task ImportImageAsync(ImagesImportParameters parameters, string localImagePath, CancellationToken cancellationToken = default(CancellationToken))
        {
            parameters.SourceName = ImportFromBodySource;
            return this.ImportImageAsync(parameters, localImagePath, null, cancellationToken);
        }

        private Task ImportImageAsync(ImagesImportParameters parameters, string localImagePath, AuthConfig authConfig, CancellationToken cancellationToken)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            HttpMethod httpMethod = HttpMethod.Get;

            BinaryRequestContent content = null;
            if (!string.IsNullOrEmpty(localImagePath))
            {
                Stream fileStream = File.OpenRead(localImagePath);
                content = new BinaryRequestContent(fileStream, TarContentType);
                httpMethod = HttpMethod.Post;
            }

            IQueryString queryParameters = new QueryString<ImagesImportParameters>(parameters);
            return this._client.MakeRequestAsync(this._client.NoErrorHandlers, httpMethod, "images/create", queryParameters, content, RegistryAuthHeaders(authConfig), cancellationToken);
        }

        private Dictionary<string, string> RegistryAuthHeaders(AuthConfig authConfig)
        {
            return new Dictionary<string, string>
            {
                {
                    RegistryAuthHeaderKey,
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(this._client.JsonSerializer.SerializeObject(authConfig ?? new AuthConfig())))
                }
            };
        }
    }
}