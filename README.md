# Code to repro MySqlConnector TimerQueue contention issue

Clone the repo onto your machine and run the following:

`docker compose build`

`docker compose up`

Wait ~10 minutes and go to `http://localhost:4040` on your browser. Choose `db-load-generator.mutex_duration` in 
the dropdown on the top.