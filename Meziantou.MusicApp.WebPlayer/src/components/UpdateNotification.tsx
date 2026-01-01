import { useServiceWorkerUpdate } from '../hooks';
import './UpdateNotification.css';

export function UpdateNotification() {
  const { needRefresh, updateServiceWorker } = useServiceWorkerUpdate();

  if (!needRefresh) {
    return null;
  }

  const handleUpdate = async () => {
    await updateServiceWorker(true);
  };

  return (
    <div className="update-notification">
      <div className="update-notification-content">
        <div className="update-notification-text">
          <strong>New version available!</strong>
          <span>A new version of the app is ready to use.</span>
        </div>
        <button
          className="update-notification-button"
          onClick={handleUpdate}
          type="button"
        >
          Restart to Update
        </button>
      </div>
    </div>
  );
}
