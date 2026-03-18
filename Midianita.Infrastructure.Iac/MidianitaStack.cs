using Amazon.CDK;
using Amazon.CDK.AWS.DynamoDB;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.Lambda.EventSources;
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

            // 3. SQS Dead Letter Queue: Midianita_Dev_AuditQueue_DLQ
            var auditDlq = new Queue(this, "AuditQueueDLQ", new QueueProps
            {
                QueueName = "Midianita_Dev_AuditQueue_DLQ",
                RetentionPeriod = Duration.Days(14) // Long life for manual debugging
            });

            // 3.1. Main SQS Queue with DLQ attached
            var auditQueue = new Queue(this, "AuditQueue", new QueueProps
            {
                QueueName = "Midianita_Dev_AuditQueue",
                VisibilityTimeout = Duration.Seconds(180),
                DeadLetterQueue = new DeadLetterQueue
                {
                    MaxReceiveCount = 3,
                    Queue = auditDlq
                }
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

            // 5. Lambdas
            var lambdaBinaryPath = System.Environment.GetEnvironmentVariable("LAMBDA_BINARY_PATH") ?? "../lambda-publish";

            var analisadorLambda = new Amazon.CDK.AWS.Lambda.Function(this, "AnalisadorBannerFunction", new Amazon.CDK.AWS.Lambda.FunctionProps
            {
                Runtime = Amazon.CDK.AWS.Lambda.Runtime.DOTNET_8,
                Handler = "Midianita.Workers.AnalisadorBanner::Midianita.Workers.AnalisadorBanner.Function::FunctionHandler",
                Code = Amazon.CDK.AWS.Lambda.Code.FromAsset($"{lambdaBinaryPath}/AnalisadorBanner"),
                MemorySize = 256,
                Timeout = Duration.Seconds(60),
                Environment = new System.Collections.Generic.Dictionary<string, string>
                {
                    { "DESIGNS_TABLE", designsTable.TableName },
                    { "ASSETS_BUCKET", assetsBucket.BucketName }
                }
            });

            var processadorLambda = new Amazon.CDK.AWS.Lambda.Function(this, "ProcessadorArteFunction", new Amazon.CDK.AWS.Lambda.FunctionProps
            {
                Runtime = Amazon.CDK.AWS.Lambda.Runtime.DOTNET_8,
                Handler = "Midianita.Workers.ProcessadorArte::Midianita.Workers.ProcessadorArte.Function::FunctionHandler",
                Code = Amazon.CDK.AWS.Lambda.Code.FromAsset($"{lambdaBinaryPath}/ProcessadorArte"),
                MemorySize = 512,
                Timeout = Duration.Seconds(120), // Must be >= SQS VisibilityTimeout (90s) for AI workloads
                Environment = new System.Collections.Generic.Dictionary<string, string>
                {
                    { "DESIGNS_TABLE", designsTable.TableName },
                    { "ASSETS_BUCKET", assetsBucket.BucketName }
                }
            });

            // 6. Permissions & Event Sources
            designsTable.GrantReadWriteData(analisadorLambda);
            designsTable.GrantReadWriteData(processadorLambda);
            assetsBucket.GrantReadWrite(analisadorLambda);
            assetsBucket.GrantReadWrite(processadorLambda);

            // AnalisadorBanner: default batching is fine (lightweight analysis)
            analisadorLambda.AddEventSource(new SqsEventSource(auditQueue));

            // ProcessadorArte: BatchSize=1 prevents parallel heavy AI jobs on the same instance;
            // CDK automatically grants ReceiveMessage + DeleteMessage + GetQueueAttributes IAM perms.
            processadorLambda.AddEventSource(new SqsEventSource(auditQueue, new SqsEventSourceProps
            {
                BatchSize = 1
            }));

            // 7. Outputs
            new CfnOutput(this, "Region", new CfnOutputProps { Value = this.Region });
            new CfnOutput(this, "QueueUrl", new CfnOutputProps { Value = auditQueue.QueueUrl });
            new CfnOutput(this, "DLQUrl",   new CfnOutputProps { Value = auditDlq.QueueUrl });
            new CfnOutput(this, "S3BucketName", new CfnOutputProps { Value = assetsBucket.BucketName });
            new CfnOutput(this, "DesignsTableName", new CfnOutputProps { Value = designsTable.TableName });
            new CfnOutput(this, "AuditTableName", new CfnOutputProps { Value = auditTable.TableName });
        }
    }
}
