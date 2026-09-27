using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Kiota.Http.HttpClientLibrary.Middleware.Options;
using Soenneker.Instagram.Accounts.Abstract;
using Soenneker.Instagram.Accounts.Options;
using Soenneker.Instagram.OpenApiClient;
using Soenneker.Instagram.OpenApiClient.Models;
using Soenneker.Instagram.OpenApiClientUtil.Abstract;

namespace Soenneker.Instagram.Accounts;

public sealed class InstagramAccountsUtil(IInstagramOpenApiClientUtil clientUtil) : IInstagramAccountsUtil
{
    public async ValueTask<GetId200Response?> Get(string accountId, string? fields = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        var client = await clientUtil.Get(cancellationToken).ConfigureAwait(false);
        return await client[accountId].GetAsync(c => c.QueryParameters.Fields = fields, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<GetIdMedia200Response?> GetPosts(string accountId, int limit = 25, string? after = null, string? fields = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        var client = await clientUtil.Get(cancellationToken).ConfigureAwait(false);
        return await client[accountId].Media.GetAsync(c =>
        {
            c.QueryParameters.Limit = limit;
            c.QueryParameters.After = after;
            c.QueryParameters.Fields = fields;
        }, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<string> PublishPost(string accountId, string? caption = null, IReadOnlyList<string>? imageUrls = null, string? videoUrl = null,
        InstagramPublishingOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        if (imageUrls is { Count: > 0 })
        {
            if (videoUrl is not null)
                throw new ArgumentException("Supply image URLs or a Reel video URL, not both.", nameof(videoUrl));
            return PublishPhotos(accountId, imageUrls, caption, options, cancellationToken);
        }
        if (videoUrl is not null)
            return PublishReel(accountId, videoUrl, caption, options: options, cancellationToken: cancellationToken);
        throw new NotSupportedException("Instagram does not support text-only posts. Supply at least one image URL or a video URL for a Reel.");
    }

    public async ValueTask<string> PublishPhoto(string accountId, string imageUrl, string? caption = null, string? altText = null,
        InstagramPublishingOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ValidateUrl(imageUrl, nameof(imageUrl));
        options = ValidateOptions(options);
        cancellationToken.ThrowIfCancellationRequested();
        var client = await clientUtil.Get(cancellationToken).ConfigureAwait(false);
        string id = await CreateContainer(client, accountId, new PostIdMediaXWwwFormUrlencodedRequest
        {
            ImageUrl = imageUrl, Caption = caption, AltText = altText
        }, cancellationToken).ConfigureAwait(false);
        return await PublishReadyContainer(client, accountId, id, options, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<string> PublishPhotos(string accountId, IReadOnlyList<string> imageUrls, string? caption = null,
        InstagramPublishingOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentNullException.ThrowIfNull(imageUrls);
        if (imageUrls.Count is < 1 or > 10)
            throw new ArgumentException("Supply between one and ten images.", nameof(imageUrls));
        var urls = new string[imageUrls.Count];
        for (int i = 0; i < urls.Length; i++)
        {
            urls[i] = imageUrls[i];
            ValidateUrl(urls[i], nameof(imageUrls));
        }
        options = ValidateOptions(options);
        if (urls.Length == 1)
            return await PublishPhoto(accountId, urls[0], caption, options: options, cancellationToken: cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        var client = await clientUtil.Get(cancellationToken).ConfigureAwait(false);
        var children = new string[urls.Length];
        for (int i = 0; i < urls.Length; i++)
        {
            children[i] = await CreateContainer(client, accountId, new PostIdMediaXWwwFormUrlencodedRequest
            {
                ImageUrl = urls[i], IsCarouselItem = true
            }, cancellationToken).ConfigureAwait(false);
            await WaitForContainer(client, children[i], options, cancellationToken).ConfigureAwait(false);
        }
        string id = await CreateContainer(client, accountId, new PostIdMediaXWwwFormUrlencodedRequest
        {
            MediaType = "CAROUSEL", Children = string.Join(',', children), Caption = caption
        }, cancellationToken).ConfigureAwait(false);
        return await PublishReadyContainer(client, accountId, id, options, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<string> PublishReel(string accountId, string videoUrl, string? caption = null, bool shareToFeed = true,
        InstagramPublishingOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ValidateUrl(videoUrl, nameof(videoUrl));
        options = ValidateOptions(options);
        cancellationToken.ThrowIfCancellationRequested();
        var client = await clientUtil.Get(cancellationToken).ConfigureAwait(false);
        string id = await CreateContainer(client, accountId, new PostIdMediaXWwwFormUrlencodedRequest
        {
            MediaType = "REELS", VideoUrl = videoUrl, Caption = caption, ShareToFeed = shareToFeed
        }, cancellationToken).ConfigureAwait(false);
        return await PublishReadyContainer(client, accountId, id, options, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<GetId200Response?> GetContainerStatus(string containerId, CancellationToken cancellationToken = default)
        => Get(containerId, "status_code,status", cancellationToken);

    public async ValueTask<string> PublishContainer(string accountId, string containerId, InstagramPublishingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);
        options = ValidateOptions(options);
        cancellationToken.ThrowIfCancellationRequested();
        var client = await clientUtil.Get(cancellationToken).ConfigureAwait(false);
        return await PublishReadyContainer(client, accountId, containerId, options, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<string> CreateContainer(InstagramOpenApiClient client, string accountId,
        PostIdMediaXWwwFormUrlencodedRequest body, CancellationToken cancellationToken)
    {
        var response = await client[accountId].Media.PostAsync(body, c => c.Options.Add(new RetryHandlerOption { MaxRetry = 0 }), cancellationToken).ConfigureAwait(false);
        return RequireId(response?.Id, "creating a media container");
    }

    private static async ValueTask<string> PublishReadyContainer(InstagramOpenApiClient client, string accountId, string containerId,
        InstagramPublishingOptions options, CancellationToken cancellationToken)
    {
        await WaitForContainer(client, containerId, options, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var response = await client[accountId].Media_publish.PostAsync(new PostIdMediaPublishXWwwFormUrlencodedRequest
        {
            CreationId = containerId
        }, c => c.Options.Add(new RetryHandlerOption { MaxRetry = 0 }), cancellationToken).ConfigureAwait(false);
        return RequireId(response?.Id, $"publishing container '{containerId}'; check account media before retrying");
    }

    private static async ValueTask WaitForContainer(InstagramOpenApiClient client, string containerId, InstagramPublishingOptions options,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.ProcessingTimeout);
        try
        {
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();
                var result = await client[containerId].GetAsync(c => c.QueryParameters.Fields = "status_code,status", timeout.Token).ConfigureAwait(false);
                switch (result?.StatusCode)
                {
                    case "FINISHED":
                        return;
                    case "IN_PROGRESS":
                        await Task.Delay(options.PollInterval, timeout.Token).ConfigureAwait(false);
                        break;
                    default:
                        throw new InvalidOperationException($"Instagram container '{containerId}' cannot be published: {result?.StatusCode ?? "missing status_code"}. {result?.Status}");
                }
            }
        }
        catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new TimeoutException($"Instagram container '{containerId}' was not ready within {options.ProcessingTimeout}. Check its status before resuming with PublishContainer.", e);
        }
    }

    private static InstagramPublishingOptions ValidateOptions(InstagramPublishingOptions? options)
    {
        options ??= new InstagramPublishingOptions();
        if (options.ProcessingTimeout <= TimeSpan.Zero || options.ProcessingTimeout.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(options), "ProcessingTimeout must be positive and within the timer's supported range.");
        if (options.PollInterval <= TimeSpan.Zero || options.PollInterval > options.ProcessingTimeout)
            throw new ArgumentOutOfRangeException(nameof(options), "PollInterval must be positive and no greater than ProcessingTimeout.");
        return options;
    }

    private static void ValidateUrl(string value, string parameterName)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("A publicly accessible HTTP(S) media URL is required.", parameterName);
    }

    private static string RequireId(string? id, string operation)
        => !string.IsNullOrWhiteSpace(id) ? id : throw new InvalidOperationException($"Instagram returned no ID when {operation}.");
}
