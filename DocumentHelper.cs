using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace MayanEDMSUtils
{
    /// <summary>
    /// Represents the response after creating a document.
    /// </summary>
    public class CreateDocumentResponse
    {
        /// <summary>
        /// Gets or sets the URL of the file.
        /// </summary>
        public required string FileUrl { get; set; }

        /// <summary>
        /// Gets or sets the URL of the document.
        /// </summary>
        public required string Url { get; set; }

        /// <summary>
        /// Gets or sets the name of the file.
        /// </summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the MIME type of the file.
        /// </summary>
        public string MimeType { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the ID of the file.
        /// </summary>
        public string FileId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the download URL of the file.
        /// </summary>
        public string DownloadUrl { get; set; } = string.Empty;
    }

    /// <summary>
    /// Provides helper methods for uploading and downloading documents.
    /// </summary>
    public class DocumentHelper
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string _user;
        private readonly string _password;

        /// <summary>
        /// Initializes a new instance of the <see cref="DocumentHelper"/> class.
        /// </summary>
        /// <param name="httpClient">The HttpClient used for making requests.</param>
        /// <param name="baseUrl">The base URL of the API.</param>
        /// <param name="username">The username for Basic Authentication.</param>
        /// <param name="password">The password for Basic Authentication.</param>
        public DocumentHelper(HttpClient httpClient, string baseUrl, string username, string password)
        {
            _httpClient = httpClient;
            _baseUrl = baseUrl;
            _user = username;
            _password = password;
        }

        /// <summary>
        /// Uploads a file to the server asynchronously.
        /// </summary>
        /// <param name="documentName">The name of the document.</param>
        /// <param name="fileStream">The file stream of the document to be uploaded.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the <see cref="CreateDocumentResponse"/>.</returns>
        /// <exception cref="ArgumentException">Thrown when the document name is null or empty.</exception>
        /// <exception cref="Exception">Thrown when the file upload was not successful.</exception>
        public async Task<CreateDocumentResponse> UploadFileAsync(string documentName, Stream fileStream)
        {
            if (string.IsNullOrEmpty(documentName))
            {
                throw new ArgumentException("Document Name cannot be null or empty", nameof(documentName));
            }

            // adding basic auth for the request
            var byteArray = Encoding.ASCII.GetBytes($"{_user}:{_password}");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));

            // 1. create the document
            var payload = new Dictionary<string, object>
            {
                { "document_type_id", 1 },
                { "label", documentName }
            };

            // Serialize the payload to JSON
            var jsonPayload = JsonSerializer.Serialize(payload);

            // Create the HttpContent
            var jsonStringContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_baseUrl}/documents/", jsonStringContent);

            var responseContent = await response.Content.ReadAsStringAsync();
            var documentData = JsonSerializer.Deserialize<JsonElement>(responseContent);

            // Console.WriteLine(" DOCUMENT DATA 54 " + documentData.ToString());

            var fileListUrl = documentData.GetProperty("file_list_url").GetString();
            var url = documentData.GetProperty("url").GetString();

            if (fileListUrl is null && url is null)
            {
                throw new ArgumentException($"Error creating ${documentName}");
            }

            // 2. upload the file
            using var content = new MultipartFormDataContent();

            // adding file
            var fileContent = new StreamContent(fileStream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(fileContent, "file_new", documentName);

            // adding query
            content.Add(new StringContent("replace"), "action_name");
            // Console.WriteLine(" DOCUMENT DATA 95 ");

            var uploadResponse = await _httpClient.PostAsync(fileListUrl, content);

            // Console.WriteLine($" DOCUMENT DATA 100 {uploadResponse.StatusCode} ");

            if (uploadResponse.StatusCode == System.Net.HttpStatusCode.Accepted)
            {
                // get the document
                var getDocResponse = await _httpClient.GetAsync(url);
                getDocResponse.EnsureSuccessStatusCode();

                var getDocResContent = await getDocResponse.Content.ReadAsStringAsync();
                var docData = JsonSerializer.Deserialize<JsonElement>(getDocResContent);

                // Console.WriteLine(" DOCUMENT DATA 108 " + docData.ToString());

                if (docData.TryGetProperty("file_latest", out JsonElement fileLatest))
                {
                    var fileName = fileLatest.GetProperty("filename").GetString();
                    var mimeType = fileLatest.GetProperty("mimetype").GetString();
                    var downloadUrl = fileLatest.GetProperty("download_url").GetString();
                    var fileId = fileLatest.GetProperty("id").GetString();

                    if (url is null || fileListUrl is null || fileName is null || mimeType is null || downloadUrl is null || fileId is null)
                    {
                        throw new Exception("File upload was not successful");
                    }

                    CreateDocumentResponse createDocResponse = new()
                    {
                        FileUrl = fileListUrl,
                        Url = url,
                        FileName = fileName,
                        MimeType = mimeType,
                        DownloadUrl = downloadUrl,
                        FileId = fileId,
                    };

                    return createDocResponse;
                }
                else
                {
                    throw new Exception("File upload was not successful");
                }
            }
            else
            {
                var resContent = await uploadResponse.Content.ReadAsStringAsync();
                throw new Exception($"Failed to upload file. Status code: {uploadResponse.StatusCode}, Response: {resContent}");
            }
        }

        /// <summary>
        /// Downloads a file from the specified URL and saves it to the specified file path asynchronously.
        /// </summary>
        /// <param name="fileUrl">The URL of the file to download.</param>
        /// <param name="destinationFilePath">The path where the downloaded file should be saved.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        /// <exception cref="ArgumentException">Thrown when the file URL or destination file path is null or empty.</exception>
        public async Task DownloadFileAsync(string fileUrl, string destinationFilePath)
        {
            if (string.IsNullOrEmpty(fileUrl))
            {
                throw new ArgumentException("File URL cannot be null or empty", nameof(fileUrl));
            }

            if (string.IsNullOrEmpty(destinationFilePath))
            {
                throw new ArgumentException("Destination file path cannot be null or empty", nameof(destinationFilePath));
            }

            var byteArray = Encoding.ASCII.GetBytes($"{_user}:{_password}");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));

            var response = await _httpClient.GetAsync(fileUrl);
            response.EnsureSuccessStatusCode();

            using var fileStream = new FileStream(destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await response.Content.CopyToAsync(fileStream);
        }

        /// <summary>
        /// Downloads a file from the specified URL and returns the file bytes asynchronously.
        /// </summary>
        /// <param name="fileUrl">The URL of the file to download.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the file bytes.</returns>
        /// <exception cref="ArgumentException">Thrown when the file URL is null or empty.</exception>
        public async Task<byte[]> GetFileAsync(string fileUrl)
        {
            if (string.IsNullOrEmpty(fileUrl))
            {
                throw new ArgumentException("File URL cannot be null or empty", nameof(fileUrl));
            }

            var byteArray = Encoding.ASCII.GetBytes($"{_user}:{_password}");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));

            var response = await _httpClient.GetAsync(fileUrl);
            response.EnsureSuccessStatusCode();

            byte[] fileBytes = await response.Content.ReadAsByteArrayAsync();

            return fileBytes;
        }
    }
}
