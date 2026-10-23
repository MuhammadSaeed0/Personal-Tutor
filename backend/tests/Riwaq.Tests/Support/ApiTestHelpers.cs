using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Riwaq.Tests.Support;

public static class ApiTestHelpers
{
    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{(int)response.StatusCode} {response.RequestMessage?.RequestUri}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    public static async Task<HttpResponseMessage> UploadPdfAsync(this HttpClient client, byte[] pdf, int subjectId = 4, string title = "Physics Textbook", string fileName = "physics.pdf")
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(pdf);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", fileName);
        form.Add(new StringContent(subjectId.ToString()), "subjectId");
        form.Add(new StringContent(title), "title");
        return await client.PostAsync("/api/books/upload", form);
    }

    /// <summary>Polls the processing status until the background worker finishes (or fails).</summary>
    public static async Task<JsonElement> WaitForProcessingAsync(this HttpClient client, Guid bookId, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(60));
        while (DateTime.UtcNow < deadline)
        {
            var status = await (await client.GetAsync($"/api/books/{bookId}/processing-status")).ReadJsonAsync();
            var bookStatus = status.GetProperty("bookStatus").GetString();
            if (bookStatus is "ready" or "readyWithWarnings" or "failed") return status;
            await Task.Delay(250);
        }
        throw new TimeoutException("Book processing did not finish in time.");
    }

    public static async Task<Guid> UploadAndProcessAsync(this HttpClient client, byte[]? pdf = null)
    {
        var upload = await (await client.UploadPdfAsync(pdf ?? TestPdf.Physics(Guid.NewGuid().ToString()))).ReadJsonAsync();
        var bookId = upload.GetProperty("id").GetGuid();
        var status = await client.WaitForProcessingAsync(bookId);
        if (status.GetProperty("bookStatus").GetString() == "failed")
            throw new InvalidOperationException("Processing failed: " + status);
        return bookId;
    }

    public static StringContent JsonBody(object value) =>
        new(JsonSerializer.Serialize(value, RiwaqApiFactory.Json), System.Text.Encoding.UTF8, "application/json");
}
