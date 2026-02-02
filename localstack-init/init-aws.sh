#!/bin/bash

# Ensure the script exits on failure
set -e

echo "Initializing LocalStack resources..."

# Create S3 Bucket
awslocal s3 mb s3://midianita-assets

# Configure CORS for S3 Bucket
awslocal s3api put-bucket-cors --bucket midianita-assets --cors-configuration '{
  "CORSRules": [
    {
      "AllowedHeaders": ["*"],
      "AllowedMethods": ["PUT", "GET", "POST"],
      "AllowedOrigins": ["*"],
      "ExposeHeaders": ["ETag"]
    }
  ]
}'

# Create SQS Queues
awslocal sqs create-queue --queue-name generation-queue
awslocal sqs create-queue --queue-name cleanup-queue

# Create DynamoDB Table
awslocal dynamodb create-table \
    --table-name Designs \
    --attribute-definitions AttributeName=Id,AttributeType=S \
    --key-schema AttributeName=Id,KeyType=HASH \
    --provisioned-throughput ReadCapacityUnits=5,WriteCapacityUnits=5

echo "LocalStack resources initialized successfully."

echo "⚡ Configurando Lambda .NET 8..."

awslocal lambda create-function \
    --function-name image-worker \
    --runtime dotnet8 \
    --handler Midianita.Worker::Midianita.Worker.Function::FunctionHandler \
    --memory-size 1024 \
    --timeout 60 \
    --role arn:aws:iam::000000000000:role/lambda-role \
    --zip-file fileb:///opt/lambda-code/function.zip \
    --environment Variables="{GCP_PROJECT_ID=midianita-dev-999999,GCP_LOCATION=us-central1,AWS_REGION=us-east-1}"

QUEUE_ARN=$(awslocal sqs get-queue-attributes --queue-url http://localhost:4566/000000000000/generation-queue --attribute-names QueueArn --query 'Attributes.QueueArn' --output text)

awslocal lambda create-event-source-mapping \
    --function-name image-worker \
    --batch-size 1 \
    --event-source-arn $QUEUE_ARN

echo "✅ Lambda .NET implantada!"
