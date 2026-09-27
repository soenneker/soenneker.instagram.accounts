using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Soenneker.Instagram.Accounts.Options;
using Soenneker.Instagram.OpenApiClient.Models;

namespace Soenneker.Instagram.Accounts.Abstract;

/// <summary>
/// Reads Instagram professional accounts and publishes media using Instagram:AccessToken.
/// The token must have content publishing permission for the chosen login flow and account.
/// Instagram requires image or video media; text-only posts are not supported. Captions are optional.
/// Media URLs must remain publicly accessible to Meta throughout processing; images must be JPEG.
/// Failed operations may leave unpublished containers. Publishing is not retried automatically;
/// after an ambiguous publish failure, check the account before retrying to avoid duplicate posts.
/// </summary>
public interface IInstagramAccountsUtil
{
    /// <summary>Gets an account or media object with optional comma-separated Graph API fields.</summary>
    ValueTask<GetId200Response?> Get(string accountId, string? fields = null, CancellationToken cancellationToken = default);

    /// <summary>Gets one page of account media. Pass the returned paging cursor as after to continue.</summary>
    ValueTask<GetIdMedia200Response?> GetPosts(string accountId, int limit = 25, string? after = null, string? fields = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes one image, an image carousel, or a Reel and returns the published media ID.
    /// Supply either 1–10 image URLs or a video URL. With neither, throws NotSupportedException before sending requests.
    /// </summary>
    ValueTask<string> PublishPost(string accountId, string? caption = null, IReadOnlyList<string>? imageUrls = null, string? videoUrl = null,
        InstagramPublishingOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Creates, waits for, and publishes an image with optional caption and alt text. Returns the published media ID.</summary>
    ValueTask<string> PublishPhoto(string accountId, string imageUrl, string? caption = null, string? altText = null,
        InstagramPublishingOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes 1–10 images in input order, using a carousel for multiple images. Validates every URL before creating containers.
    /// Waits for all children and the carousel before publishing. Returns the published media ID.
    /// </summary>
    ValueTask<string> PublishPhotos(string accountId, IReadOnlyList<string> imageUrls, string? caption = null,
        InstagramPublishingOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Publishes a publicly accessible video as a Reel, optionally sharing it to the feed. No image is required. Returns the published media ID.</summary>
    ValueTask<string> PublishReel(string accountId, string videoUrl, string? caption = null, bool shareToFeed = true,
        InstagramPublishingOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Gets a container's status_code and status, useful for inspecting a failed or timed-out operation.</summary>
    ValueTask<GetId200Response?> GetContainerStatus(string containerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Waits until a previously created container is FINISHED, then publishes it and returns the media ID.
    /// Throws for ERROR, EXPIRED, PUBLISHED, missing, or unknown statuses; timeout errors include the container ID.
    /// Use this to resume a timed-out container instead of creating another one. Do not publish carousel children individually.
    /// </summary>
    ValueTask<string> PublishContainer(string accountId, string containerId, InstagramPublishingOptions? options = null,
        CancellationToken cancellationToken = default);
}
