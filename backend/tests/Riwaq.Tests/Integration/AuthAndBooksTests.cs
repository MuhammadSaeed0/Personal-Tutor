using System.Net;
using System.Net.Http.Json;
using Riwaq.Tests.Support;

namespace Riwaq.Tests.Integration;

[Collection(nameof(ApiCollection))]
public class AuthTests(RiwaqApiFactory factory)
{
    [Fact]
    public async Task Register_login_refresh_and_logout()
    {
        var email = $"auth-{Guid.NewGuid():N}@example.com";
        var client = factory.CreateClient(new() { HandleCookies = true });

        var register = await client.PostAsJsonAsync("/api/auth/register", new { email, password = "Passw0rd-123", displayName = "Lina" });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        var profile = (await register.ReadJsonAsync()).GetProperty("profile");
        Assert.Equal("ar", profile.GetProperty("preferences").GetProperty("uiLanguage").GetString()); // Arabic by default

        var duplicate = await client.PostAsJsonAsync("/api/auth/register", new { email, password = "Passw0rd-123", displayName = "Lina" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var badLogin = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "wrong-password1" });
        Assert.Equal(HttpStatusCode.Unauthorized, badLogin.StatusCode);

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Passw0rd-123" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        // Refresh requires the anti-CSRF client header and the HttpOnly cookie.
        var noHeader = await client.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.BadRequest, noHeader.StatusCode);

        var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        refreshRequest.Headers.Add("X-Riwaq-Client", "web");
        var refresh = await client.SendAsync(refreshRequest);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);

        var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logoutRequest.Headers.Add("X-Riwaq-Client", "web");
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(logoutRequest)).StatusCode);

        var afterLogout = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        afterLogout.Headers.Add("X-Riwaq-Client", "web");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(afterLogout)).StatusCode);
    }

    [Fact]
    public async Task Protected_endpoints_require_authentication()
    {
        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/books")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/api/subjects")).StatusCode);
    }

    [Fact]
    public async Task Seven_subjects_are_seeded()
    {
        var subjects = await (await factory.CreateClient().GetAsync("/api/subjects")).ReadJsonAsync();
        var codes = subjects.EnumerateArray().Select(s => s.GetProperty("code").GetString()).ToList();
        Assert.Equal(["arabic", "english", "french", "physics", "chemistry", "biology", "geology"], codes);
    }
}

[Collection(nameof(ApiCollection))]
public class BookTests(RiwaqApiFactory factory)
{
    [Fact]
    public async Task Upload_processes_pdf_with_page_references_and_chapters()
    {
        var client = await factory.CreateStudentAsync();
        var bookId = await client.UploadAndProcessAsync();

        var detail = await (await client.GetAsync($"/api/books/{bookId}")).ReadJsonAsync();
        Assert.Equal(4, detail.GetProperty("book").GetProperty("pageCount").GetInt32());

        var chapters = detail.GetProperty("chapters").EnumerateArray().ToList();
        Assert.Equal(2, chapters.Count);
        Assert.Equal("Chapter 1: Forces and Motion", chapters[0].GetProperty("title").GetString());
        Assert.Equal(1, chapters[0].GetProperty("startPage").GetInt32());
        Assert.Equal(2, chapters[0].GetProperty("endPage").GetInt32());
        Assert.Equal(3, chapters[1].GetProperty("startPage").GetInt32());

        var page2 = await (await client.GetAsync($"/api/books/{bookId}/pages/2/text")).ReadJsonAsync();
        Assert.Contains("m g sin(theta)", page2.GetProperty("text").GetString());

        var file = await client.GetAsync($"/api/books/{bookId}/file");
        Assert.Equal("application/pdf", file.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Duplicate_upload_is_detected()
    {
        var client = await factory.CreateStudentAsync();
        var pdf = TestPdf.Physics("duplicate-check");
        var first = await (await client.UploadPdfAsync(pdf)).ReadJsonAsync();
        var second = await client.UploadPdfAsync(pdf);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var problem = await second.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("duplicate_book", problem.GetProperty("code").GetString());
        Assert.Equal(first.GetProperty("id").GetGuid(), problem.GetProperty("bookId").GetGuid());
    }

    [Fact]
    public async Task Invalid_files_are_rejected()
    {
        var client = await factory.CreateStudentAsync();
        var notPdf = await client.UploadPdfAsync("hello world"u8.ToArray());
        Assert.Equal(HttpStatusCode.BadRequest, notPdf.StatusCode);

        var wrongExtension = await client.UploadPdfAsync(TestPdf.Physics("ext"), fileName: "book.exe");
        Assert.Equal(HttpStatusCode.BadRequest, wrongExtension.StatusCode);

        var badSubject = await client.UploadPdfAsync(TestPdf.Physics("subject"), subjectId: 999);
        Assert.Equal(HttpStatusCode.BadRequest, badSubject.StatusCode);
    }

    [Fact]
    public async Task Scanned_pages_are_reported_not_silently_dropped()
    {
        var client = await factory.CreateStudentAsync();
        var upload = await (await client.UploadPdfAsync(TestPdf.ImageOnly(), title: "Scanned")).ReadJsonAsync();
        var bookId = upload.GetProperty("id").GetGuid();
        var status = await client.WaitForProcessingAsync(bookId);
        Assert.Equal("readyWithWarnings", status.GetProperty("bookStatus").GetString());

        var detail = await (await client.GetAsync($"/api/books/{bookId}")).ReadJsonAsync();
        var codes = detail.GetProperty("warnings").EnumerateArray().Select(w => w.GetProperty("code").GetString()).ToList();
        Assert.Contains("scanned_page_no_ocr", codes);
        Assert.Contains("no_text_extracted", codes);
    }

    [Fact]
    public async Task Students_cannot_access_each_others_books()
    {
        var owner = await factory.CreateStudentAsync();
        var intruder = await factory.CreateStudentAsync();
        var bookId = await owner.UploadAndProcessAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await intruder.GetAsync($"/api/books/{bookId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.GetAsync($"/api/books/{bookId}/file")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.GetAsync($"/api/books/{bookId}/pages/1/text")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.DeleteAsync($"/api/books/{bookId}")).StatusCode);
        Assert.Empty((await (await intruder.GetAsync("/api/books")).ReadJsonAsync()).EnumerateArray());

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/books/{bookId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/books/{bookId}")).StatusCode);
    }
}

[CollectionDefinition(nameof(ApiCollection))]
public class ApiCollection : ICollectionFixture<RiwaqApiFactory>;
