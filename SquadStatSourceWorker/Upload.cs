using System;
using System.IO;
using System.Collections.Generic;
using Amazon.S3;
using Amazon.S3.Transfer;
using Newtonsoft.Json.Linq;

namespace SquadStatSourceWorker
{
    public class Uploader
    {
        public string AWSAccessKey { get; }
        public string AWSSecretKey { get; }

        public IAmazonS3 S3 { get; }

        public List<string> FilesUploading = new List<string>();

        public Uploader(JObject Config)
        {
            AWSAccessKey = Config["aws_access_key"].ToString();
            AWSSecretKey = Config["aws_secret_key"].ToString();

            S3 = new AmazonS3Client(AWSAccessKey, AWSSecretKey, Amazon.RegionEndpoint.USWest2);
        }

        public void Upload(string Path, string FileName)
        {
            var Utility = new TransferUtility(S3);
            var Request = new TransferUtilityUploadRequest
            {
                BucketName = "squadstats-raw",
                Key = "year=" + Squad.Server.CurrentMatch.MatchStart.Year + "/"
                    + "month=" + Squad.Server.CurrentMatch.MatchStart.Month + "/"
                    + FileName,
                FilePath = Path + "\\" + FileName,
            };
            var UploadTask = Utility.UploadAsync(Request);
            UploadTask.ContinueWith(Result => {
                if (Result.Status == System.Threading.Tasks.TaskStatus.Faulted)
                {
                    FilesUploading.Remove(System.IO.Path.GetFileNameWithoutExtension(FileName));
                    Console.WriteLine(Result.Exception);
                }
                else if (Result.Status == System.Threading.Tasks.TaskStatus.RanToCompletion)
                {
                    File.Delete(Path + "\\" + FileName);
                    Console.WriteLine("Uploaded " + FileName);
                }
            });
        }

        public void UploadDiff(string Path)
        {
            string[] FilePaths = Directory.GetFiles(Path, "*.parquet", SearchOption.TopDirectoryOnly);
            foreach (var FilePath in FilePaths)
            {
                if (!FilesUploading.Contains(System.IO.Path.GetFileNameWithoutExtension(FilePath)))
                {
                    FilesUploading.Add(System.IO.Path.GetFileNameWithoutExtension(FilePath));
                    Upload(System.IO.Path.GetDirectoryName(FilePath), System.IO.Path.GetFileName(FilePath));
                }
            }
        }
    }
}