using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Client.Framework.Release;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Client.Tests.EditMode
{
    [Category("D1")]
    public sealed class NativeBuildProfileTests
    {
        private static Type ProfileType()
        { var type = Type.GetType("Client.Framework.Release.NativeBuildProfile, Client.Framework"); Assert.That(type, Is.Not.Null, "A stable native DATA profile is required."); return type; }
        private static object Invoke(MethodInfo method, object target, params object[] args)
        { try { return method.Invoke(target, args); } catch(TargetInvocationException error) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; } }
        private static object Parse(string json) => Invoke(ProfileType().GetMethod("Parse"), null, json);
        private static JObject Data(RSA key, bool acceptance = false)
        {
            var publicKey = key.ExportParameters(false);
            return new JObject { ["schemaVersion"] = 1, ["keyId"] = "sgame-release", ["modulus"] = Convert.ToBase64String(publicKey.Modulus), ["exponent"] = Convert.ToBase64String(publicKey.Exponent),
                ["endpoint"] = "http://127.0.0.1:18765/", ["acceptance"] = acceptance, ["packageId"] = "com.sgame.client.d1", ["sessionHost"] = "127.0.0.1", ["sessionPort"] = 7000 };
        }
        [Test] public void NativePublicDataPinsVerifyOnlyItsSignerWhileDefaultRemainsClosed()
        {
            using(var key = new RSACryptoServiceProvider(2048) { PersistKeyInCsp = false }) using(var other = new RSACryptoServiceProvider(2048) { PersistKeyInCsp = false })
            {
                object profile = Parse(Data(key).ToString());
                var verifier = (ManifestVerifier)Invoke(ProfileType().GetMethod("CreateVerifier", BindingFlags.Instance | BindingFlags.NonPublic), profile);
                byte[] channel = Encoding.UTF8.GetBytes(new JObject { ["schemaVersion"] = 1, ["keyId"] = "sgame-release", ["releaseId"] = "v1", ["manifestRelativePath"] = "release.json", ["manifestSha256"] = new string('a',64) }.ToString());
                byte[] signature = key.SignData(channel, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                Assert.That(verifier.VerifyChannel(channel, signature).ReleaseId, Is.EqualTo("v1"));
                Assert.Throws<ReleaseVerificationException>(() => new ManifestVerifier().VerifyChannel(channel, signature));
                Assert.Throws<ReleaseVerificationException>(() => verifier.VerifyChannel(channel, other.SignData(channel, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)));
                Invoke(ProfileType().GetMethod("RequireSigner"), profile, key);
                Assert.Throws<InvalidOperationException>(() => Invoke(ProfileType().GetMethod("RequireSigner"), profile, other));
            }
        }
        [TestCase("private"), TestCase("unknown"), TestCase("duplicate"), TestCase("empty"), TestCase("oversize")]
        public void NativeProfileRejectsUnboundedOrUnexpectedTrustMaterial(string mutation)
        {
            using(var key = new RSACryptoServiceProvider(2048) { PersistKeyInCsp = false })
            {
                var json = Data(key); if(mutation == "private") json["d"] = "private"; if(mutation == "unknown") json["dynamicTrustUrl"] = "https://example.invalid";
                if(mutation == "empty") json["modulus"] = "";
                string text = mutation == "oversize" ? new string(' ', 20000) : json.ToString();
                if(mutation == "duplicate") text = text.Insert(1, "\"keyId\":\"other\",");
                Assert.Throws<System.IO.InvalidDataException>(() => Parse(text));
            }
        }
    }
}
