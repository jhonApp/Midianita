using Amazon.Lambda.Core;

namespace Midianita.Worker
{
    public class ConsoleLambdaContext : ILambdaContext
    {
        public string AwsRequestId => Guid.NewGuid().ToString();
        public IClientContext ClientContext => null;
        public string FunctionName => "ConsoleWorker";
        public string FunctionVersion => "1.0";
        public ICognitoIdentity Identity => null;
        public string InvokedFunctionArn => "arn:aws:lambda:us-east-1:000000000000:function:ConsoleWorker";
        public ILambdaLogger Logger => new ConsoleLambdaLogger();
        public string LogGroupName => "ConsoleGroup";
        public string LogStreamName => "ConsoleStream";
        public int MemoryLimitInMB => 1024;
        public TimeSpan RemainingTime => TimeSpan.FromMinutes(1);
    }

    public class ConsoleLambdaLogger : ILambdaLogger
    {
        public void Log(string message) => Console.WriteLine(message);
        public void LogLine(string message) => Console.WriteLine(message);
    }
}
