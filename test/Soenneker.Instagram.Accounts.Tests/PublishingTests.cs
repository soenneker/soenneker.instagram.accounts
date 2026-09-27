using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;
using Soenneker.Instagram.Accounts.Options;
using Soenneker.Instagram.OpenApiClientUtil.Abstract;

namespace Soenneker.Instagram.Accounts.Tests;

public sealed class PublishingTests
{
    private static readonly InstagramPublishingOptions Fast = new() { PollInterval = TimeSpan.FromMilliseconds(1), ProcessingTimeout = TimeSpan.FromSeconds(5) };
    private const string Ready = """{"status_code":"FINISHED"}""";

    [Test]
    public async Task Photo_encodes_caption_and_waits_before_publishing()
    {
        using var f = new Fixture("""{"id":"container"}""", """{"status_code":"IN_PROGRESS"}""", Ready, """{"id":"post"}""");
        Check(await f.Util.PublishPhoto("account", "https://example.com/image.jpg?x=1&y=2", "Hello & + café", "Alt text", Fast) == "post");
        var requests = f.Handler.Requests;
        Check(requests.Count == 4);
        Check(requests[0].Path == "/v26.0/account/media" && requests[0].Method == "POST");
        Check(requests[0].Form["caption"] == "Hello & + café" && requests[0].Form["alt_text"] == "Alt text");
        Check(requests[0].Form["image_url"] == "https://example.com/image.jpg?x=1&y=2");
        Check(!requests[0].Form.ContainsKey("media_type"));
        Check(requests[1].Path == "/v26.0/container" && requests[1].Query.Contains("status_code,status"));
        Check(requests[3].Path == "/v26.0/account/media_publish" && requests[3].Form["creation_id"] == "container");
    }

    [Test]
    public async Task Carousel_preserves_order_and_publishes_only_parent()
    {
        using var f = new Fixture("""{"id":"one"}""", Ready, """{"id":"two"}""", Ready, """{"id":"parent"}""", Ready, """{"id":"post"}""");
        Check(await f.Util.PublishPost("account", "Caption", ["https://example.com/1.jpg", "https://example.com/2.jpg"], options: Fast) == "post");
        var r = f.Handler.Requests;
        Check(r.Count == 7 && r[0].Form["is_carousel_item"] == "true" && r[2].Form["is_carousel_item"] == "true");
        Check(r[4].Form["media_type"] == "CAROUSEL" && r[4].Form["children"] == "one,two" && r[4].Form["caption"] == "Caption");
        Check(r.Count(x => x.Path.EndsWith("media_publish")) == 1 && r[6].Form["creation_id"] == "parent");
    }

    [Test]
    public async Task Single_image_post_works_without_caption()
    {
        using var f = new Fixture("""{"id":"container"}""", Ready, """{"id":"post"}""");
        Check(await f.Util.PublishPost("account", imageUrls: ["https://example.com/1.jpg"], options: Fast) == "post");
        Check(!f.Handler.Requests[0].Form.ContainsKey("caption") && !f.Handler.Requests[0].Form.ContainsKey("children"));
    }

    [Test]
    public async Task Reel_works_without_image_or_caption()
    {
        using var f = new Fixture("""{"id":"container"}""", Ready, """{"id":"reel"}""");
        Check(await f.Util.PublishPost("account", videoUrl: "https://example.com/video.mp4", options: Fast) == "reel");
        var body = f.Handler.Requests[0].Form;
        Check(body["media_type"] == "REELS" && body["video_url"] == "https://example.com/video.mp4" && body["share_to_feed"] == "true");
        Check(!body.ContainsKey("image_url") && !body.ContainsKey("caption"));
    }

    [Test]
    public async Task Text_only_is_rejected_before_requests()
    {
        using var f = new Fixture();
        await Throws<NotSupportedException>(() => f.Util.PublishPost("account", "Text only").AsTask());
        Check(f.Handler.Requests.Count == 0);
    }

    [Test]
    public async Task Invalid_batches_and_options_do_not_create_containers()
    {
        using var f = new Fixture();
        await Throws<ArgumentException>(() => f.Util.PublishPhotos("account", ["https://example.com/1.jpg", "file:///bad.jpg"]).AsTask());
        await Throws<ArgumentException>(() => f.Util.PublishPhotos("account", []).AsTask());
        await Throws<ArgumentException>(() => f.Util.PublishPhotos("account", Enumerable.Repeat("https://example.com/1.jpg", 11).ToArray()).AsTask());
        await Throws<ArgumentException>(() => f.Util.PublishPost("account", imageUrls: ["https://example.com/1.jpg"], videoUrl: "https://example.com/1.mp4").AsTask());
        await Throws<ArgumentOutOfRangeException>(() => f.Util.PublishPhoto("account", "https://example.com/1.jpg", options: new() { PollInterval = TimeSpan.Zero }).AsTask());
        Check(f.Handler.Requests.Count == 0);
    }

    [Test]
    [Arguments("ERROR")]
    [Arguments("EXPIRED")]
    [Arguments("PUBLISHED")]
    [Arguments("UNKNOWN")]
    [Arguments("")]
    public async Task Nonready_status_prevents_publish(string status)
    {
        using var f = new Fixture("""{"id":"container"}""", "{\"status_code\":\"" + status + "\",\"status\":\"details\"}");
        var e = await Throws<InvalidOperationException>(() => f.Util.PublishReel("account", "https://example.com/video.mp4", options: Fast).AsTask());
        Check(e.Message.Contains("container") && f.Handler.Requests.Count == 2);
    }

    [Test]
    public async Task Missing_container_id_stops_immediately()
    {
        using var f = new Fixture("{}");
        await Throws<InvalidOperationException>(() => f.Util.PublishPhoto("account", "https://example.com/image.jpg").AsTask());
        Check(f.Handler.Requests.Count == 1);
    }

    [Test]
    public async Task Missing_published_id_does_not_report_success()
    {
        using var f = new Fixture(Ready, "{}");
        var e = await Throws<InvalidOperationException>(() => f.Util.PublishContainer("account", "container", Fast).AsTask());
        Check(e.Message.Contains("container") && f.Handler.Requests.Count == 2);
    }

    [Test]
    public async Task Timeout_includes_container_and_does_not_publish()
    {
        using var f = new Fixture("""{"status_code":"IN_PROGRESS"}""");
        var options = new InstagramPublishingOptions { PollInterval = TimeSpan.FromMilliseconds(50), ProcessingTimeout = TimeSpan.FromMilliseconds(50) };
        var e = await Throws<TimeoutException>(() => f.Util.PublishContainer("account", "existing", options).AsTask());
        Check(e.Message.Contains("existing") && f.Handler.Requests.All(x => x.Method == "GET"));
    }

    [Test]
    public async Task Cancellation_during_processing_stops_before_publish()
    {
        using var f = new Fixture("""{"status_code":"IN_PROGRESS"}""");
        using var cts = new CancellationTokenSource();
        f.Handler.AfterRequest = () => cts.Cancel();
        await Throws<OperationCanceledException>(() => f.Util.PublishContainer("account", "existing", cancellationToken: cts.Token).AsTask());
        Check(f.Handler.Requests.Count == 1);
    }

    [Test]
    public async Task Precancelled_publish_makes_no_requests()
    {
        using var f = new Fixture();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Throws<OperationCanceledException>(() => f.Util.PublishPhoto("account", "https://example.com/1.jpg", cancellationToken: cts.Token).AsTask());
        Check(f.Handler.Requests.Count == 0);
    }

    [Test]
    public async Task Resume_publishes_existing_container_without_creating_another()
    {
        using var f = new Fixture(Ready, """{"id":"post"}""");
        Check(await f.Util.PublishContainer("account", "existing", Fast) == "post");
        Check(f.Handler.Requests.Count == 2 && f.Handler.Requests[1].Form["creation_id"] == "existing");
    }

    [Test]
    public async Task Api_failure_propagates_without_publishing()
    {
        using var f = new Fixture("""{"error":{"message":"Denied","code":10}}""");
        f.Handler.StatusCode = HttpStatusCode.BadRequest;
        await Throws<Microsoft.Kiota.Abstractions.ApiException>(() => f.Util.PublishPhoto("account", "https://example.com/1.jpg").AsTask());
        Check(f.Handler.Requests.Count == 1);
    }

    private static void Check(bool condition)
    {
        if (!condition) throw new Exception("Request contract assertion failed.");
    }

    private static async Task<T> Throws<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T e) { return e; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }

    private sealed class Fixture : IDisposable
    {
        public Handler Handler { get; }
        public InstagramAccountsUtil Util { get; }
        private readonly HttpClient _http;
        private readonly HttpClientRequestAdapter _adapter;
        public Fixture(params string[] responses)
        {
            Handler = new Handler(responses);
            _http = new HttpClient(Handler);
            _adapter = new HttpClientRequestAdapter(new AnonymousAuthenticationProvider(), httpClient: _http) { BaseUrl = "https://graph.facebook.com/v26.0" };
            Util = new InstagramAccountsUtil(new Provider(new OpenApiClient.InstagramOpenApiClient(_adapter)));
        }
        public void Dispose() { _adapter.Dispose(); _http.Dispose(); }
    }

    private sealed class Provider(OpenApiClient.InstagramOpenApiClient client) : IInstagramOpenApiClientUtil
    {
        public ValueTask<OpenApiClient.InstagramOpenApiClient> Get(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(client);
        }
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed record Request(string Method, string Path, string Query, Dictionary<string, string> Form);

    private sealed class Handler(string[] responses) : HttpMessageHandler
    {
        public List<Request> Requests { get; } = [];
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
        public Action? AfterRequest { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            var form = new Dictionary<string, string>();
            foreach (string part in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] pair = part.Split('=', 2);
                form[WebUtility.UrlDecode(pair[0])] = WebUtility.UrlDecode(pair.Length == 2 ? pair[1] : "");
            }
            Requests.Add(new Request(request.Method.Method, request.RequestUri!.AbsolutePath, Uri.UnescapeDataString(request.RequestUri.Query), form));
            if (Requests.Count > responses.Length) throw new Exception("Unexpected extra HTTP request.");
            AfterRequest?.Invoke();
            return new HttpResponseMessage(StatusCode) { Content = new StringContent(responses[Requests.Count - 1], System.Text.Encoding.UTF8, "application/json") };
        }
    }
}
