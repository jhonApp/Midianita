using Amazon.CDK;
using Amazon.CDK.AWS.DynamoDB;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.AWS.SQS;
using Constructs;

namespace Midianita.Infrastructure.IaC
{
    public class MidianitaInfrastructureIaCStack : Stack
    {
        internal MidianitaInfrastructureIaCStack(Construct scope, string id, IStackProps props = null) : base(scope, id, props)
        {
            // 1. DynamoDB: Midianita_Dev_Designs
            var designsTable = new Table(this, "DesignsTable", new TableProps
            {
                TableName = "Midianita_Dev_Designs",
                PartitionKey = new Amazon.CDK.AWS.DynamoDB.Attribute { Name = "Id", Type = AttributeType.STRING },
                BillingMode = BillingMode.PAY_PER_REQUEST,
                RemovalPolicy = RemovalPolicy.DESTROY
            });

            designsTable.AddGlobalSecondaryIndex(new GlobalSecondaryIndexProps
            {
                IndexName = "UserIdIndex",
                PartitionKey = new Amazon.CDK.AWS.DynamoDB.Attribute { Name = "UserId", Type = AttributeType.STRING },
                ProjectionType = ProjectionType.ALL
            });

            // 2. DynamoDB: Midianita_Dev_AuditLogs
            var auditTable = new Table(this, "AuditTable", new TableProps
            {
                TableName = "Midianita_Dev_AuditLogs",
                PartitionKey = new Amazon.CDK.AWS.DynamoDB.Attribute { Name = "LogId", Type = AttributeType.STRING },
                SortKey = new Amazon.CDK.AWS.DynamoDB.Attribute { Name = "Timestamp", Type = AttributeType.STRING },
                BillingMode = BillingMode.PAY_PER_REQUEST,
                RemovalPolicy = RemovalPolicy.DESTROY
            });

            // 3. SQS Queue: Midianita_Dev_AuditQueue
            var auditQueue = new Queue(this, "AuditQueue", new QueueProps
            {
                QueueName = "Midianita_Dev_AuditQueue",
                VisibilityTimeout = Duration.Seconds(30)
            });

            // 4. S3 Bucket: midianita-dev-assets
            var assetsBucket = new Bucket(this, "AssetsBucket", new BucketProps
            {
                BucketName = "midianita-dev-assets",
                AutoDeleteObjects = true,
                RemovalPolicy = RemovalPolicy.DESTROY,
                Cors = new ICorsRule[]
                {
                    new CorsRule
                    {
                        AllowedOrigins = new[] { "*" },
                        AllowedMethods = new[] { HttpMethods.GET, HttpMethods.PUT, HttpMethods.POST, HttpMethods.DELETE, HttpMethods.HEAD },
                        ExposedHeaders = new[] { "ETag" }
                    }
                }
            });

            // 5. Outputs
            new CfnOutput(this, "Region", new CfnOutputProps { Value = this.Region });
            new CfnOutput(this, "QueueUrl", new CfnOutputProps { Value = auditQueue.QueueUrl });
            new CfnOutput(this, "S3BucketName", new CfnOutputProps { Value = assetsBucket.BucketName });
            new CfnOutput(this, "DesignsTableName", new CfnOutputProps { Value = designsTable.TableName });
            new CfnOutput(this, "AuditTableName", new CfnOutputProps { Value = auditTable.TableName });
        }
    }
}
