using Amazon.DynamoDBv2;
using Amazon.Lambda.Core;
using Amazon.SimpleNotificationService;
using Moq;
using ind_tableau_alert_dashboard_risk_queue;
using Amazon.DynamoDBv2.Model;
using Amazon.SimpleNotificationService.Model;
using System.Net;
using StackExchange.Redis;

namespace ind_tableau_alert_dashboard_risk_queue_unittest
{
    public class FunctionTests
    {
        private readonly Mock<IAmazonDynamoDB> mockDynamoDB;
        private readonly Mock<ILambdaContext> mockContext;
        private readonly Mock<IAmazonSimpleNotificationService> mockSNS;
        private readonly Mock<IConnectionMultiplexer> mockRedisMultiplexer = new Mock<IConnectionMultiplexer>();
        private readonly Mock<IDatabase> mockDatabase;

        private readonly Function targetFunction;

        public FunctionTests()
        {
            mockDynamoDB = new Mock<IAmazonDynamoDB>();
            mockSNS = new Mock<IAmazonSimpleNotificationService>();
            mockContext = new Mock<ILambdaContext>();
            mockDatabase = new Mock<IDatabase>();
            mockRedisMultiplexer.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(mockDatabase.Object);

            targetFunction = new Function(mockDynamoDB.Object, mockSNS.Object, mockRedisMultiplexer.Object);
        }

        [Fact(DisplayName = "test1")]
        public async Task PublishMessage_Should_Publish_Message_To_SNS()
        {
            // Arrange
            var publishResponse = new PublishResponse
            {
                HttpStatusCode = HttpStatusCode.OK
            };

            mockSNS.Setup(sns => sns.PublishAsync(It.IsAny<PublishRequest>(), default))
                .ReturnsAsync(publishResponse);

            // Act
            var result = await targetFunction.PublishMessage("message");

            // Assert
            Assert.Equal(HttpStatusCode.OK, result);
        }

        [Fact(DisplayName = "test2")]
        public async Task GetAlerts_DynamoDB_ValidRequest_ReturnsListOfSitePairs()
        {
            // Arrange
            var scanResponse = new ScanResponse
            {
                Items = new List<Dictionary<string, AttributeValue>>
                {
                    new Dictionary<string, AttributeValue>
                    {
                        { "SitePair", new AttributeValue { S = "sitePair1" } }
                    },
                    new Dictionary<string, AttributeValue>
                    {
                        { "SitePair", new AttributeValue { S = "sitePair2" } }
                    }
                }
            };

            mockDynamoDB.Setup(db => db.ScanAsync(It.IsAny<ScanRequest>(), default))
                .ReturnsAsync(scanResponse);

            // Act
            var result = await targetFunction.GetAlerts_DynamoDB();

            // Assert
            Assert.Equal(scanResponse.Items.Count, result.Count);
            Assert.Equal(scanResponse.Items[0]["SitePair"].S, result[0]);
            Assert.Equal(scanResponse.Items[1]["SitePair"].S, result[1]);
        }

        [Fact(DisplayName = "test3")]
        public async Task GetAlerts_Redis_ValidRequest_ReturnsListOfSitePairs()
        {
            // Arrange
            string indexKey = "alertdashboardsitepairqueuerisk:index";

            var keys = new RedisValue[] { "sitePair1", "sitePair2" };

            mockDatabase
                .Setup(db => db.SetMembersAsync((RedisKey)indexKey, It.IsAny<CommandFlags>()))
                .ReturnsAsync(keys);

            // Act
            var result = await targetFunction.GetAlerts_Redis();

            // Assert
            Assert.Equal(keys.Length, result.Count);
            Assert.Equal(keys[0], result[0]);
            Assert.Equal(keys[1], result[1]);
        }

        [Fact(DisplayName = "test4")]
        public async Task DeleteAlert_Redis_ValidRequest_CallsDeleteItemAsync()
        {
            // Arrange
            var sitepair = "testsitepair";

            string indexKey = "alertdashboardsitepairqueuerisk:index";
            string key = $"alertdashboardsitepairqueuerisk:sitepair:{sitepair}";

            mockDatabase
                .Setup(db => db.SetRemoveAsync((RedisKey)indexKey, It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            mockDatabase
                .Setup(db => db.KeyDeleteAsync((RedisKey)key, It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            // Act
            await targetFunction.DeleteAlert_Redis(sitepair);

            // Assert
            mockDatabase.Verify(db =>
                db.SetRemoveAsync((RedisKey)indexKey, It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()), Times.Once);
            mockDatabase.Verify(db =>
                db.KeyDeleteAsync((RedisKey)key, It.IsAny<CommandFlags>()), Times.Once);
        }

        [Fact(DisplayName = "test5")]
        public async Task DeleteAlert_DynamoDB_ValidRequest_CallsDeleteItemAsync()
        {
            // Arrange
            var sitepair = "testsitepair";

            var deleteItemResponse = new DeleteItemResponse
            {
                HttpStatusCode = HttpStatusCode.OK
            };

            mockDynamoDB.Setup(db => db.DeleteItemAsync(It.IsAny<DeleteItemRequest>(), default))
                .ReturnsAsync(deleteItemResponse);

            // Act
            await targetFunction.DeleteAlert_DynamoDB(sitepair);

            // Assert
            mockDynamoDB.Verify(db => db.DeleteItemAsync(It.IsAny<DeleteItemRequest>(), default), Times.Once);
        }
    }
}