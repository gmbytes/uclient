#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Client.Contracts;
using Client.Framework.Release;
using Client.Framework.Resource;
using Client.Framework.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityGameFramework.Runtime;
using Object=UnityEngine.Object;
using SessionState=Client.Contracts.SessionState;

namespace Client.Tests
{
    // Exercises actual ResourceService/GF Editor asset callbacks and real UIService construction;
    // it does not claim native Package initialization or native acceptance.
    [Category("F8")]
    public sealed class NativeClientCompositionTests
    {
        private GameObject host,slotObject,invalidRoot;
        private ResourceSlotBootstrap slot;
        private const string Catalog="Assets/Content/UI/Common/UIViewDefinitions.asset";
        [UnitySetUp] public IEnumerator SetUp()
        {
            GameEntry.Shutdown(ShutdownType.None);yield return null;
            host=new GameObject("F8 actual resource adapter fixture");host.SetActive(false);host.AddComponent<BaseComponent>();host.AddComponent<EventComponent>();
            var editor=host.AddComponent<EditorResourceComponent>();Set(editor,"m_MinLoadAssetRandomDelaySeconds",0f);Set(editor,"m_MaxLoadAssetRandomDelaySeconds",0f);
            var resources=host.AddComponent<ResourceComponent>();host.AddComponent<ObjectPoolComponent>();var ui=host.AddComponent<UIComponent>();Set(ui,"m_UIGroups",Array.CreateInstance(typeof(UIComponent).GetNestedType("UIGroup",BindingFlags.NonPublic),0));
            host.SetActive(true);yield return null;
            slotObject=new GameObject("F8 initialized resource adapter binding");slot=slotObject.AddComponent<ResourceSlotBootstrap>();
            Set(slot,"<ResourceComponent>k__BackingField",resources);Set(slot,"<Service>k__BackingField",new ResourceService(resources));
            ((TaskCompletionSource<bool>)typeof(ResourceSlotBootstrap).GetField("initialization",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(slot)).SetResult(true);
        }
        [UnityTearDown] public IEnumerator TearDown(){GameEntry.Shutdown(ShutdownType.None);if(host!=null)Object.Destroy(host);if(slotObject!=null)Object.Destroy(slotObject);if(invalidRoot!=null)Object.Destroy(invalidRoot);yield return null;}
        private static void Set(object target,string field,object value)=>target.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);
        private static IEnumerator Terminal(Task task){float until=Time.realtimeSinceStartup+5;while(!task.IsCompleted&&Time.realtimeSinceStartup<until)yield return null;Assert.That(task.IsCompleted,Is.True);}
        private NativeClientComposition Composition(OwnedSession session,string address=Catalog,bool invalid=false)
        {var prefab=invalid?(invalidRoot=new GameObject("F8 invalid root")):AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Content/UI/Common/AdaptiveUIRoot.prefab");return new NativeClientComposition(prefab,address,new Log(),CancellationToken.None,session);}
        [UnityTest] public IEnumerator FailedCatalogLoadReleasesAcceptedSessionAndRetainsBothErrors()
        {
            var session=new OwnedSession{Throw=true};var task=Composition(session,"Assets/NoSuchF8Catalog.asset").ComposeAsync(slot,CancellationToken.None);yield return Terminal(task);
            Assert.That(session.Disposals,Is.EqualTo(1),"Accepted composition owns the supplied session even if catalog load fails.");Assert.That(task.IsFaulted,Is.True);
            var errors=task.Exception.Flatten().InnerExceptions;Assert.That(errors.Any(e=>e is InvalidOperationException&&e.Message.Contains("Cannot load asset")),Is.True);Assert.That(errors.Any(e=>e is IOException&&e.Message=="F8 session cleanup"),Is.True);
        }
        [UnityTest] public IEnumerator CancelledCatalogLoadReleasesAcceptedSession()
        {
            var session=new OwnedSession();using(var cancel=new CancellationTokenSource()){cancel.Cancel();var task=Composition(session).ComposeAsync(slot,cancel.Token);yield return Terminal(task);Assert.That(task.IsCanceled,Is.True);Assert.That(session.Disposals,Is.EqualTo(1));}
        }
        [UnityTest] public IEnumerator FailedUiConstructionAttemptsSessionAndRootCleanupWithoutMaskingCause()
        {
            var session=new OwnedSession{Throw=true};var task=Composition(session,invalid:true).ComposeAsync(slot,CancellationToken.None);yield return Terminal(task);yield return null;
            Assert.That(session.Disposals,Is.EqualTo(1));Assert.That(task.IsFaulted,Is.True);var errors=task.Exception.Flatten().InnerExceptions;
            Assert.That(errors.Any(e=>e is ArgumentNullException),Is.True);Assert.That(errors.Any(e=>e is IOException),Is.True);Assert.That(GameObject.Find("F8 invalid root(Clone)"),Is.Null);
        }
        [UnityTest] public IEnumerator SuccessfulContextOwnsSessionOnceAndDuplicateCannotDisposeIt()
        {
            var session=new OwnedSession();var composition=Composition(session);var task=composition.ComposeAsync(slot,CancellationToken.None);yield return Terminal(task);Assert.That(task.IsCompletedSuccessfully,Is.True,task.Exception?.ToString());
            try{var duplicate=composition.ComposeAsync(slot,CancellationToken.None);yield return Terminal(duplicate);Assert.That(duplicate.IsFaulted,Is.True);Assert.That(session.Disposals,Is.Zero);((IDisposable)task.Result).Dispose();((IDisposable)task.Result).Dispose();Assert.That(session.Disposals,Is.EqualTo(1));}
            finally{((IDisposable)task.Result).Dispose();}
        }
        private sealed class OwnedSession:ISessionClient,IDisposable
        {public int Disposals;public bool Throw;public event Action<SessionState> StateChanged{add{}remove{}}public Task<LoginResult> LoginAsync(LoginInput input,CancellationToken token)=>throw new NotSupportedException();public void Disconnect(){}public void Dispose(){Disposals++;if(Throw)throw new IOException("F8 session cleanup");}}
        private sealed class Log:ILogService{public void Write(ClientLogLevel level,string code,string message){}}
    }
}
#endif
