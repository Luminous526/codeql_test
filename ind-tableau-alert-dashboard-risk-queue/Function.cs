using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.Core;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace ind_tableau_alert_dashboard_risk_queue
{
    public class Function
    {
        IAmazonDynamoDB DynamoDB { get; set; }
        IAmazonSimpleNotificationService ClientSNS { get; set; }

        IConnectionMultiplexer RedisMultiplexer { get; set; }

        string isDynamoDBDisabled = "";

        private const string AlertDashboardQueueTable = "AlertDashboardSitePairQueueRisk";

        public Function()
        {
            DynamoDB = new AmazonDynamoDBClient();
            ClientSNS = new AmazonSimpleNotificationServiceClient();
            string redisConnectionString = Environment.GetEnvironmentVariable("Redis_Endpoint") ?? "";
            RedisMultiplexer = ConnectionMultiplexer.Connect(redisConnectionString);
        }

        public Function(IAmazonDynamoDB dynamoDB, IAmazonSimpleNotificationService clientSNS, IConnectionMultiplexer redisMultiplexer)
        {
            this.DynamoDB = dynamoDB;
            this.ClientSNS = clientSNS;
            this.RedisMultiplexer = redisMultiplexer;
        }

        public async Task FunctionHandler()
        {
            isDynamoDBDisabled = Environment.GetEnvironmentVariable("DisableDynamoDB") ?? "";

            try
            {
                Console.WriteLine("Beging Process...");

                var request = new ScanRequest
                {
                    TableName = AlertDashboardQueueTable,
                };

                List<string> sitepairList = await GetAlerts_Redis();

                if (isDynamoDBDisabled != "true")
                {
                    List<string> sitepairList_DynamoDB = await GetAlerts_DynamoDB();

                    foreach (string sitepair in sitepairList_DynamoDB)
                    {
                        if (!sitepairList.Contains(sitepair))
                        {
                            sitepairList.Add(sitepair);
                        }
                    }
                }

                foreach (string sitePair in sitepairList)
                {
                    var publishMessageRespone = await PublishMessage(sitePair);
                    Console.WriteLine("PublishMessage StatusCode: " + publishMessageRespone);

                    await DeleteAlert_Redis(sitePair);

                    if (isDynamoDBDisabled != "true")
                    {
                        await DeleteAlert_DynamoDB(sitePair);
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Error Message: " + e.Message);
                Console.WriteLine("Error StackTrace: " + e.StackTrace);
            }
        }

        public async Task<System.Net.HttpStatusCode> PublishMessage(string message)
        {
            string topicArn = ""; //AlertDashboardRiskScoreAlertQueue
            if (System.Environment.GetEnvironmentVariable("RiskScoreAlertQueueTopicArn") != null)
            {
                topicArn = System.Environment.GetEnvironmentVariable("RiskScoreAlertQueueTopicArn");
            }

            var request = new PublishRequest
            {
                TopicArn = topicArn,
                Message = message,
            };

            try
            {
                PublishResponse response = await ClientSNS.PublishAsync(request);

                return response.HttpStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);

                return System.Net.HttpStatusCode.InternalServerError;
            }
        }

        public async Task<List<string>> GetAlerts_DynamoDB()
        {
            var request = new ScanRequest
            {
                TableName = AlertDashboardQueueTable,
            };

            List<string> sitePairs = new List<string>();

            try
            {
                ScanResponse response = await DynamoDB.ScanAsync(request);
                foreach (Dictionary<string, AttributeValue> attributeList in response.Items)
                {
                    var sitePair = attributeList["SitePair"].S;
                    sitePairs.Add(sitePair);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Error Message: " + e.Message);
                Console.WriteLine("Error StackTrace: " + e.StackTrace);
            }

            return sitePairs;
        }

        public async Task<List<string>> GetAlerts_Redis()
        {
            string indexKey = "alertdashboardsitepairqueuerisk:index";

            List<string> sitePairs = new List<string>();

            try
            {
                var db = RedisMultiplexer.GetDatabase();

                var keys = await db.SetMembersAsync(indexKey);

                foreach (var key in keys)
                {
                    sitePairs.Add(key.ToString());
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Error Message: " + e.Message);
                Console.WriteLine("Error StackTrace: " + e.StackTrace);
            }

            return sitePairs;
        }

        public async Task DeleteAlert_DynamoDB(string sitepair)
        {
            var tablKey = new Dictionary<string, AttributeValue>()
            {
                { "SitePair", new AttributeValue { S = sitepair } }
            };
            var deleteItemRequest = new DeleteItemRequest
            {
                TableName = AlertDashboardQueueTable,
                Key = tablKey
            };
            var deleteResponse = await DynamoDB.DeleteItemAsync(deleteItemRequest);
            Console.WriteLine($"Scan And Delete DeleteItem StatusCode: {sitepair} - {deleteResponse.HttpStatusCode}");
        }

        public async Task DeleteAlert_Redis(string sitepair)
        {
            string indexKey = "alertdashboardsitepairqueuerisk:index";
            string key = $"alertdashboardsitepairqueuerisk:sitepair:{sitepair}";

            var db = RedisMultiplexer.GetDatabase();

            //delete key from set
            await db.SetRemoveAsync(indexKey, key);

            //delete hash
            await db.KeyDeleteAsync(key);

            Console.WriteLine("DeleteAlert_Redis: " + key);
        }
    }
}
