using Amazon.CDK;
using Amazon.CDK.AWS.DynamoDB;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.Lambda.EventSources;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.AWS.SQS;
// NOVO: Namespace para leitura segura de parâmetros do SSM Parameter Store
using Amazon.CDK.AWS.SSM;
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

            // -----------------------------------------------------------
            // NOVO: (1, 2, 3) Filas SQS para Geração de Imagem
            // -----------------------------------------------------------
            var imageGenerationDlq = new Queue(this, "ImageGenerationDLQ", new QueueProps
            {
                QueueName = "Midianita_Dev_ImageGenerationQueue_DLQ",
                RetentionPeriod = Duration.Days(14)
            });

            var imageGenerationQueue = new Queue(this, "ImageGenerationQueue", new QueueProps
            {
                QueueName = "Midianita_Dev_ImageGenerationQueue",
                VisibilityTimeout = Duration.Seconds(180),
                DeadLetterQueue = new DeadLetterQueue
                {
                    MaxReceiveCount = 3,
                    Queue = imageGenerationDlq
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
                // CORREÇÃO: O VisibilityTimeout da fila (180s) PRECISA SER MAIOR OU IGUAL ao Timeout da Lambda.
                Timeout = Duration.Seconds(180),  
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

            // NOVO: Importando a tabela Midianita_Dev_Job externa e dando permissão + variável de ambiente
            var jobTable = Table.FromTableName(this, "JobTable", "Midianita_Dev_Job");
            jobTable.GrantReadWriteData(processadorLambda);
            processadorLambda.AddEnvironment("DYNAMODB_JOB_TABLE", jobTable.TableName);

            // --------------------------------------------------------------------------------------
            // NOVO: Importando a tabela Midianita_Dev_Banner externa e dando permissão IAM para a Lambda
            // --------------------------------------------------------------------------------------
            var bannerTable = Table.FromTableName(this, "BannerTable", "Midianita_Dev_Banner");
            bannerTable.GrantReadData(processadorLambda);
            processadorLambda.AddEnvironment("DYNAMODB_BANNER_TABLE", bannerTable.TableName);

            // --------------------------------------------------------------------------------------
            // NOVO: Leitura segura da chave de API Fal.ai diretamente do SSM Parameter Store
            // O valor NUNCA toca o código-fonte — o CDK resolve o token em deploy time.
            // --------------------------------------------------------------------------------------
            var falApiKey = StringParameter.ValueForStringParameter(this, "/Midianita/FalApiKey");
            processadorLambda.AddEnvironment("FAL_KEY", falApiKey);

            // AnalisadorBanner: default batching is fine (lightweight analysis)
            analisadorLambda.AddEventSource(new SqsEventSource(auditQueue));

            // ProcessadorArte: BatchSize=1 prevents parallel heavy AI jobs on the same instance;
            // NOVO: (4) Alterado de auditQueue para imageGenerationQueue
            processadorLambda.AddEventSource(new SqsEventSource(imageGenerationQueue, new SqsEventSourceProps
            {
                BatchSize = 1
            }));

            // 7. Outputs
            new CfnOutput(this, "Region", new CfnOutputProps { Value = this.Region });
            new CfnOutput(this, "QueueUrl", new CfnOutputProps { Value = auditQueue.QueueUrl });
            new CfnOutput(this, "DLQUrl",   new CfnOutputProps { Value = auditDlq.QueueUrl });
            // NOVO: (5) Output da Fila Principal de Geração de Imagem
            new CfnOutput(this, "ImageGenerationQueueUrl", new CfnOutputProps { Value = imageGenerationQueue.QueueUrl });
            new CfnOutput(this, "S3BucketName", new CfnOutputProps { Value = assetsBucket.BucketName });
            new CfnOutput(this, "DesignsTableName", new CfnOutputProps { Value = designsTable.TableName });
            new CfnOutput(this, "AuditTableName", new CfnOutputProps { Value = auditTable.TableName });
        }
    }
}
