using System;
using Google.Protobuf.WellKnownTypes;
using Google.Protobuf;

class Program {
    static void Main() {
        var json = "{ \"predictions\": [ { \"bytesBase64Encoded\": \"aGVsbG8=\" } ] }";
        var parsed = JsonParser.Default.Parse<Struct>(json);
        var predictions = parsed.Fields["predictions"].ListValue;
        var prediction = predictions.Values[0];
        
        if (prediction.KindCase == Value.KindOneofCase.StructValue) {
            var b64 = prediction.StructValue.Fields["bytesBase64Encoded"].StringValue;
            var bytes = Convert.FromBase64String(b64);
            Console.WriteLine("b64: " + b64);
            Console.WriteLine("Bytes: " + BitConverter.ToString(bytes));
        }
    }
}
