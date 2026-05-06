#[cfg(target_os = "macos")]
use tauri::menu::{Menu, MenuEvent, MenuItem, PredefinedMenuItem, Submenu};
use tauri::{AppHandle, Emitter, Manager};

const PLAYER_CONTROL_EVENT: &str = "player-menu-control";
const MENU_VOLUME_STEP: f64 = 0.05;

#[cfg(target_os = "macos")]
const PLAYER_SUBMENU_ID: &str = "player_menu";
#[cfg(target_os = "macos")]
const MENU_ITEM_CURRENT_TRACK: &str = "player_current_track";
#[cfg(target_os = "macos")]
const MENU_ITEM_PREVIOUS: &str = "player_previous";
#[cfg(target_os = "macos")]
const MENU_ITEM_NEXT: &str = "player_next";
#[cfg(target_os = "macos")]
const MENU_ITEM_VOLUME: &str = "player_volume";
#[cfg(target_os = "macos")]
const MENU_ITEM_VOLUME_DOWN: &str = "player_volume_down";
#[cfg(target_os = "macos")]
const MENU_ITEM_VOLUME_UP: &str = "player_volume_up";

#[derive(Clone, serde::Serialize)]
#[serde(rename_all = "camelCase")]
struct PlayerMenuControlEvent {
  action: &'static str,
  delta: Option<f64>,
}

#[cfg(target_os = "macos")]
struct PlayerMenuItems {
  current_track_item: MenuItem<tauri::Wry>,
  volume_item: MenuItem<tauri::Wry>,
}

#[tauri::command]
fn update_menu_bar_state(
  app: AppHandle,
  current_track: Option<String>,
  volume: f64,
) -> Result<(), String> {
  #[cfg(target_os = "macos")]
  {
    let Some(menu_items) = app.try_state::<PlayerMenuItems>() else {
      return Ok(());
    };

    menu_items
      .current_track_item
      .set_text(&format_current_track_label(current_track.as_deref()))
      .map_err(|err| err.to_string())?;
    menu_items
      .volume_item
      .set_text(&format_volume_label(volume))
      .map_err(|err| err.to_string())?;
  }

  #[cfg(not(target_os = "macos"))]
  {
    let _ = (app, current_track, volume);
  }

  Ok(())
}

#[cfg(target_os = "macos")]
fn format_current_track_label(current_track: Option<&str>) -> String {
  let track = current_track.unwrap_or("None").replace('\n', " ");
  let trimmed = track.trim();
  let shortened = if trimmed.chars().count() > 80 {
    let prefix: String = trimmed.chars().take(77).collect();
    format!("{prefix}...")
  } else {
    trimmed.to_owned()
  };

  format!("Current track: {shortened}")
}

#[cfg(target_os = "macos")]
fn format_volume_label(volume: f64) -> String {
  let clamped = volume.clamp(0.0, 2.0);
  let percent = (clamped * 100.0).round();
  format!("Volume: {percent:.0}%")
}

#[cfg(target_os = "macos")]
fn emit_player_control_event(
  app_handle: &AppHandle<tauri::Wry>,
  action: &'static str,
  delta: Option<f64>,
) {
  let payload = PlayerMenuControlEvent { action, delta };
  if let Err(err) = app_handle.emit(PLAYER_CONTROL_EVENT, payload) {
    eprintln!("failed to emit player menu control event: {err}");
  }
}

#[cfg(target_os = "macos")]
fn handle_player_menu_event(app_handle: &AppHandle<tauri::Wry>, event: MenuEvent) {
  match event.id.as_ref() {
    MENU_ITEM_PREVIOUS => emit_player_control_event(app_handle, "previous", None),
    MENU_ITEM_NEXT => emit_player_control_event(app_handle, "next", None),
    MENU_ITEM_VOLUME_UP => emit_player_control_event(app_handle, "volume", Some(MENU_VOLUME_STEP)),
    MENU_ITEM_VOLUME_DOWN => emit_player_control_event(app_handle, "volume", Some(-MENU_VOLUME_STEP)),
    _ => {}
  }
}

#[cfg(target_os = "macos")]
fn setup_player_menu(app: &tauri::App<tauri::Wry>) -> tauri::Result<()> {
  let current_track_item =
    MenuItem::with_id(app, MENU_ITEM_CURRENT_TRACK, "Current track: None", false, None::<&str>)?;
  let previous_item = MenuItem::with_id(app, MENU_ITEM_PREVIOUS, "Previous", true, None::<&str>)?;
  let next_item = MenuItem::with_id(app, MENU_ITEM_NEXT, "Next", true, None::<&str>)?;
  let volume_item = MenuItem::with_id(app, MENU_ITEM_VOLUME, "Volume: 100%", false, None::<&str>)?;
  let volume_down_item =
    MenuItem::with_id(app, MENU_ITEM_VOLUME_DOWN, "Volume down", true, None::<&str>)?;
  let volume_up_item = MenuItem::with_id(app, MENU_ITEM_VOLUME_UP, "Volume up", true, None::<&str>)?;
  let separator_1 = PredefinedMenuItem::separator(app)?;
  let separator_2 = PredefinedMenuItem::separator(app)?;

  let player_submenu = Submenu::with_id_and_items(
    app,
    PLAYER_SUBMENU_ID,
    "Player",
    true,
    &[
      &current_track_item,
      &separator_1,
      &previous_item,
      &next_item,
      &separator_2,
      &volume_item,
      &volume_down_item,
      &volume_up_item,
    ],
  )?;

  let app_menu = Menu::default(app.handle())?;
  app_menu.append(&player_submenu)?;
  app.set_menu(app_menu)?;
  app.manage(PlayerMenuItems {
    current_track_item: current_track_item.clone(),
    volume_item: volume_item.clone(),
  });

  app.on_menu_event(handle_player_menu_event);

  Ok(())
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
  tauri::Builder::default()
    .setup(|app| {
      #[cfg(target_os = "macos")]
      setup_player_menu(app)?;

      Ok(())
    })
    .invoke_handler(tauri::generate_handler![update_menu_bar_state])
    .run(tauri::generate_context!())
    .expect("error while running tauri application");
}
