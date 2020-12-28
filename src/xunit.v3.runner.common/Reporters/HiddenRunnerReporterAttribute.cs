using System;

namespace Xunit.Runner.Common
{
	/// <summary>
	/// This class is used to decorate runner reporters which should not be considered during the normal
	/// reporter enumeration and selection process. This includes <see cref="DefaultRunnerReporter"/>
	/// (since it's the default fallback) or any reporter which is activated outside the normal
	/// runner selection process (like <see cref="TcpRunnerReporter"/>).
	/// </summary>
	[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
	public class HiddenRunnerReporterAttribute : Attribute
	{ }
}
