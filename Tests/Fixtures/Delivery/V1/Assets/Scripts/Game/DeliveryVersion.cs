using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Client.Contracts;
using Client.Game.Protocol.AcceptanceV1;
using Google.Protobuf;
namespace Client.Game
{
    public static class DeliveryVersion
    {
        private struct Value { public int Number; }
        public static void Run(ILogService log)
        {
            var message=new BaselineMessage { Number=ulong.MaxValue,Items={new BaselineItem{Value="list"}},ById={[7]=new BaselineItem{Value="map"}} };
            byte[] wire=message.ToByteArray().Concat(new byte[]{0xA0,0x06,0x7B}).ToArray();var parsed=BaselineMessage.Parser.ParseFrom(wire);
            if(parsed.Number!=ulong.MaxValue||parsed.Items.Clone()[0].Value!="list"||parsed.ById.Clone()[7].Value!="map"||!parsed.ToByteArray().SequenceEqual(wire))throw new InvalidOperationException("Baseline Parser capability failed.");
            if(new List<Value>{new Value{Number=42}}[0].Number!=42||new Dictionary<int,IMessage>{{1,message}}[1]==null||Task.FromResult(new Value{Number=42}).Result.Number!=42)throw new InvalidOperationException("Hot generic capability failed.");
            if(new Client.Game.Protocol.GameMessageRegistry().TryGetParser(101,out _))throw new InvalidOperationException("Formal business admission must remain empty.");
            log?.Write(ClientLogLevel.Info,"CLIENT_CODE_VERSION","v1");log?.Write(ClientLogLevel.Info,"CLIENT_PARSER_EXECUTED","BaselineMessage list,map,unknown-fields,uint64,hot-generics,empty-registry");
        }
    }
}
