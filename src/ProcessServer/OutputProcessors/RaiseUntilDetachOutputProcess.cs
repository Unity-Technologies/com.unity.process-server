namespace Unity.ProcessServer.Server
{
	using Editor.Tasks;

	class RaiseUntilDetachOutputProcess : BaseOutputListProcessor<string>
	{
		private bool detached = false;

		public void Detach()
		{
			detached = true;
		}

		protected override void RaiseOnEntry(string entry)
		{
			if (!detached)
				base.RaiseOnEntry(entry);
		}
	}
}
