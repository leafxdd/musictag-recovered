using System.Windows.Forms;
using MusicTagWinApp.Win32.Taskbar;

namespace MusicTagWinApp.Instances;

internal class TaskbarProgressController
{
	private readonly Form ownerForm;

	private readonly ITaskbarList4 taskbarList;

	private TaskbarProgressBarStatus? currentStatus;

	public TaskbarProgressController(Form ownerForm)
	{
		this.ownerForm = ownerForm;
		taskbarList = (ITaskbarList4)new CTaskbarList();
	}

	public void SetProgressValue(int current, int total)
	{
		if (!ownerForm.IsDisposed)
		{
			taskbarList.SetProgressValue(ownerForm.Handle, (ulong)current, (ulong)total);
		}
	}

	public void SetProgressState(TaskbarProgressBarStatus status)
	{
		if (!ownerForm.IsDisposed && currentStatus != status)
		{
			taskbarList.SetProgressState(ownerForm.Handle, status);
			currentStatus = status;
		}
	}
}

