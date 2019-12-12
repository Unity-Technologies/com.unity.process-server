using System;
using System.Runtime.CompilerServices;
using System.Collections;
using System.Threading;
using Unity.Editor.Tasks;
using System.Threading.Tasks;

namespace BaseTests
{
    using Unity.Editor.ProcessServer.Internal.IO;
    using Unity.Editor.Tasks.Extensions;

    public partial class BaseTest
	{
        internal SPath TestAssemblyLocation => System.Reflection.Assembly.GetExecutingAssembly().Location.ToSPath().Parent;
        internal SPath ServerDirectory => TestAssemblyLocation;

        public const bool TracingEnabled = false;

        protected async Task RunTest(Func<IEnumerator> testMethodToRun)
		{
            var scheduler = (new ThreadSynchronizationContext(default)).FromSynchronizationContext();
            var taskStart = new Task<IEnumerator>(testMethodToRun);
			taskStart.Start(scheduler);
			var e = await RunOn(testMethodToRun, scheduler);
			while (await RunOn(s => ((IEnumerator)s).MoveNext(), e, scheduler))
			{ }
		}

		private Task<T> RunOn<T>(Func<T> method, TaskScheduler scheduler)
		{
			return Task<T>.Factory.StartNew(method, CancellationToken.None, TaskCreationOptions.None, scheduler);
		}

		private Task<T> RunOn<T>(Func<object, T> method, object state, TaskScheduler scheduler)
		{
			return Task<T>.Factory.StartNew(method, state, CancellationToken.None, TaskCreationOptions.None, scheduler);
		}

        private SPath? testApp;
        internal SPath TestApp
        {
            get
            {
                if (!testApp.HasValue)
                    testApp = System.Reflection.Assembly.GetExecutingAssembly().Location.ToSPath().Parent.Combine("Helper.CommandLine.exe");
                return testApp.Value;
            }
        }
    }
}
