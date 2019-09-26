using System;
using Amazon.S3;
using Amazon.Runtime;
using Amazon.S3.Transfer;
using Newtonsoft.Json.Linq;

namespace SquadStatSourceWorker
{
    public class Uploader
    {
        public string AWSAccessKey { get; }
        public string AWSSecretKey { get; }

        public IAmazonS3 S3 { get; }

        public Uploader(JObject Config)
        {
            AWSAccessKey = Config["aws_access_key"].ToString();
            AWSSecretKey = Config["aws_secret_key"].ToString();

            var Credentials = new BasicAWSCredentials(AWSAccessKey, AWSSecretKey);
            S3 = new AmazonS3Client(Credentials, Amazon.RegionEndpoint.USWest1);
        }

        public void Upload(string FileName)
        {
            var Utility = new TransferUtility(S3);
            var Request = new TransferUtilityUploadRequest
            {
                BucketName = "squadstats",
                Key = FileName,
                FilePath = FileName
            };
            var UploadTask = Utility.UploadAsync(Request);
            UploadTask.ContinueWith(antecedent => Console.WriteLine(antecedent.Exception));
        }
    }
}