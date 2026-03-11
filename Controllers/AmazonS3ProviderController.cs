using Amazon;
using BitMiracle.LibTiff.Classic;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
// using Syncfusion.EJ2.SpellChecker;
// using EJ2APIServices;
using SkiaSharp;
using Syncfusion.DocIO;
// using Syncfusion.EJ2.DocumentEditor;
using Syncfusion.DocIO.DLS;
using Syncfusion.EJ2.FileManager.AmazonS3FileProvider;
using Syncfusion.EJ2.FileManager.Base;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using WDocument = Syncfusion.DocIO.DLS.WordDocument;
using WFormatType = Syncfusion.DocIO.FormatType;

namespace EJ2AmazonS3ASPCoreFileProvider.Controllers
{


    [Route("api/[controller]")]
    [EnableCors("AllowAllOrigins")]
    public class AmazonS3ProviderController : Controller
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        public AmazonS3FileProvider operation;
        public string basePath;
        protected RegionEndpoint bucketRegion;

        public AmazonS3ProviderController(IWebHostEnvironment hostingEnvironment, IHostEnvironment env, IConfiguration configuration)
        {
            this._configuration = configuration;
            this.basePath = hostingEnvironment.ContentRootPath;
            this.basePath = basePath.Replace("../", "");
            this.operation = new AmazonS3FileProvider(env);

            var bucketName = this._configuration["DMSS3Bucket"] ?? string.Empty;
            var accessKey = this._configuration["S3BucketAccessKey"] ?? string.Empty;
            var secretKey = this._configuration["S3BucketSecretKey"] ?? string.Empty;
            var region = this._configuration["S3Region"] ?? "ap-south-1";

            this.operation.RegisterAmazonS3(bucketName, accessKey, secretKey, region);
        }

        [Route("GeneratePreSignedUrl")]
        public IActionResult GeneratePreSignedUrl(string docPath)
        {
            return Ok(this.operation.GeneratePreSignedUrl($"{this.operation.rootFolder}{docPath}", 60));
        }

        [HttpPost("AmazonS3FileOperations")]
        public object AmazonS3FileOperations([FromBody] FileManagerDirectoryContent args)
        {
            this.operation.SetRules(GetRules());
            if (args.Action == "delete" || args.Action == "rename")
            {
                if ((args.TargetPath == null) && (args.Path == ""))
                {
                    FileManagerResponse response = new FileManagerResponse();
                    ErrorDetails er = new ErrorDetails
                    {
                        Code = "401",
                        Message = "Restricted to modify the root folder."
                    };
                    response.Error = er;
                    return this.operation.ToCamelCase(response);
                }
            }
            switch (args.Action)
            {
                case "read":
                    // reads the file(s) or folder(s) from the given path.
                    return this.operation.ToCamelCase(this.operation.GetFiles(args.Path, false, args.Names, args.Data));
                case "delete":
                    // deletes the selected file(s) or folder(s) from the given path.
                    return this.operation.ToCamelCase(this.operation.Delete(args.Path, args.Names, args.Data));
                case "copy":
                    // copies the selected file(s) or folder(s) from a path and then pastes them into a given target path.
                    return this.operation.ToCamelCase(this.operation.Copy(args.Path, args.TargetPath, args.Names, args.RenameFiles, args.TargetData, args.Data));
                case "move":
                    // cuts the selected file(s) or folder(s) from a path and then pastes them into a given target path.
                    return this.operation.ToCamelCase(this.operation.Move(args.Path, args.TargetPath, args.Names, args.RenameFiles, args.TargetData, args.Data));
                case "details":
                    // gets the details of the selected file(s) or folder(s).
                    return this.operation.ToCamelCase(this.operation.Details(args.Path, args.Names, args.Data));
                case "create":
                    // creates a new folder in a given path.
                    return this.operation.ToCamelCase(this.operation.Create(args.Path, args.Name, args.Data));
                case "search":
                    // gets the list of file(s) or folder(s) from a given path based on the searched key string.
                    return this.operation.ToCamelCase(this.operation.Search(args.Path, args.SearchString, args.ShowHiddenItems, args.CaseSensitive, args.Data));
                case "rename":
                    // renames a file or folder.
                    return this.operation.ToCamelCase(this.operation.Rename(args.Path, args.Name, args.NewName, false, args.ShowFileExtension, args.Data));
            }
            return null;
        }

        // uploads the file(s) into a specified path
        [HttpPost("AmazonS3Upload")]
        public async Task<IActionResult> AmazonS3Upload(string path, IList<IFormFile> uploadFiles, string action, string data)
        {
            FileManagerResponse uploadResponse;
            FileManagerDirectoryContent[] dataObject = new FileManagerDirectoryContent[1];
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };
            if (!string.IsNullOrWhiteSpace(data))
            {
                dataObject[0] = JsonConvert.DeserializeObject<FileManagerDirectoryContent>(data);
            }
            else
            {
                dataObject[0] = new FileManagerDirectoryContent(); // Or handle default logic
            }
            foreach (var file in uploadFiles)
            {
                var folders = (file.FileName).Split('/');
                // checking the folder upload
                if (folders.Length > 1)
                {
                    for (var i = 0; i < folders.Length - 1; i++)
                    {
                        if (!this.operation.checkFileExist(path, folders[i]))
                        {
                            this.operation.ToCamelCase(this.operation.Create(path, folders[i], dataObject));
                        }
                        path += folders[i] + "/";
                    }
                }
            }
            int chunkIndex = int.TryParse(HttpContext.Request.Form["chunk-index"], out int parsedChunkIndex) ? parsedChunkIndex : 0;
            int totalChunk = int.TryParse(HttpContext.Request.Form["total-chunk"], out int parsedTotalChunk) ? parsedTotalChunk : 0;
            uploadResponse = operation.Upload(path, uploadFiles, action, dataObject, chunkIndex, totalChunk);
            if (uploadResponse.Error != null)
            {
                Response.Clear();
                Response.ContentType = "application/json; charset=utf-8";
                Response.StatusCode = Convert.ToInt32(uploadResponse.Error.Code);
                Response.HttpContext.Features.Get<IHttpResponseFeature>().ReasonPhrase = uploadResponse.Error.Message;
                //  return Content("");

            }

            var uploadedFilesDetails = new List<object>();

            // ✅ Fetch uploaded file details
            foreach (var file in uploadFiles)
            {
                var actualFileName = Path.GetFileName(file.FileName);

                var s3FilePath = $"{path}{actualFileName}"; // Full S3 Path

                var fileDetails = new
                {
                    FileName = actualFileName,
                    FilePath = $"{path}{actualFileName}",
                    path = path,
                    FileSize = file.Length,
                    FileType = file.ContentType,
                    Extension = Path.GetExtension(actualFileName),
                    UploadDate = DateTime.UtcNow,
                    UniqueHash = GenerateUniqueId(s3FilePath),
                    IsEncrypted = false, // Set based on encryption status
                    ParentFolder = GetParentFolder(s3FilePath), // Parent folder reference
                    ThumbnailPath = "", // Generate if applicable
                    LastModified = DateTime.UtcNow,
                    Tags = "document, confidential, invoice",
                    DownloadCount = 0,
                };

                uploadedFilesDetails.Add(fileDetails);

            }

            return Ok(JsonConvert.SerializeObject(uploadedFilesDetails, new JsonSerializerSettings
            {
                ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
            }));


        }

        public string GetParentFolder(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return null;

            // Trim trailing slashes to avoid empty segments
            filePath = filePath.TrimEnd('/');

            var parts = filePath.Split('/');

            // Ensure there is at least one parent folder before extracting
            return parts.Length > 1 ? parts[^2] : null;
        }

        public static string GenerateUniqueId(string path)
        {
            using (var sha256 = SHA256.Create())
            {
                var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(path));
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
            }
        }


        // downloads the selected file(s) and folder(s)
        [HttpPost("AmazonS3Download")]
        public IActionResult AmazonS3Download(string downloadInput)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };
            FileManagerDirectoryContent args = JsonConvert.DeserializeObject<FileManagerDirectoryContent>(downloadInput);
            return operation.Download(args.Path, args.Names);
        }

        // gets the image(s) from the given path
        [HttpGet("AmazonS3GetImage")]
        public IActionResult AmazonS3GetImage(FileManagerDirectoryContent args)
        {
            return operation.GetImage(args.Path, args.Id, false, null, args.Data);
        }

        public AccessDetails GetRules()
        {
            AccessDetails accessDetails = new AccessDetails();
            List<AccessRule> Rules = new List<AccessRule> {
                // Deny writing for particular file
                new AccessRule { Path = "Pictures/Employees/Adam.png", Role = "Document Manager", Read = Permission.Allow, Write = Permission.Deny, Copy = Permission.Deny, Download = Permission.Deny, IsFile = true },
                // Deny based on the type
                new AccessRule { Path = "Music/", Role = "Document Manager", Write = Permission.Deny, WriteContents = Permission.Deny, Upload = Permission.Allow, UploadContentFilter = UploadContentFilter.FoldersOnly, IsFile = false },
            };
            accessDetails.AccessRules = Rules;
            accessDetails.Role = "Document Manager";
            return accessDetails;
        }
    }

}
