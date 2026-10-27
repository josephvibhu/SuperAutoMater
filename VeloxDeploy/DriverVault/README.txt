VELOX DEPLOY: DRIVERVAULT REPOSITORY
=====================================

Drop offline driver packages (.inf, .sys, .cat) into their respective brand folders:

- Lenovo\
  e.g. Lenovo\ThinkPad_T480\
  e.g. Lenovo\ThinkPad_T490\
- Dell\
  e.g. Dell\Latitude_5490\
  e.g. Dell\Latitude_7490\
- HP\
  e.g. HP\EliteBook_840_G5\
- Generic\
  Storage\  (Intel RST / AMD VMD NVMe drivers)
  Network\  (Intel Gigabit & Realtek Wi-Fi / Ethernet)
  Audio\    (Realtek HD Audio Codecs)

Velox Deploy will automatically match the laptop's motherboard model and inject the drivers offline via DISM before first boot.
