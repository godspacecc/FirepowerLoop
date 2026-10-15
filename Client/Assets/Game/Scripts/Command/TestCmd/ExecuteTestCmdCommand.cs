using System.Threading;
using System.Threading.Tasks;
using QFramework;

namespace Game
{
    public sealed class ExecuteTestCmdCommand : AbstractCommand<Task<TestCmdExecutionResult>>
    {
        public string RawText;
        public CancellationToken CancellationToken;

        protected override Task<TestCmdExecutionResult> OnExecute()
        {
            return this.GetSystem<TestCmdSystem>().ExecuteAsync(RawText, CancellationToken);
        }
    }
}
