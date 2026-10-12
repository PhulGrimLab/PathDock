@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run_ui_tests.ps1" %*
