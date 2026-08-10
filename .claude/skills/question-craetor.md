---
name: create-question
description: Browse localhost:5678/app/login page via Chrome DevTools MCP, login with teacher credentials. Then navigate to the "Test Ekleme" (/exam) page. Fill the "Yeni Test Oluştur" form with book and exam details.
---

Browse localhost:5678/app/login page via Chrome DevTools MCP, login with teacher credentials. Then navigate to the "Test Ekleme" (/exam) page. Fill the "Yeni Test Oluştur" form with book and exam details:
- Select a book from the dropdown, selet last book. (e.g., "Matematik 9. Sınıf").
- Select a test from the dropdown (e.g., "Test 1"). IF drop down is empty then change the book and select a different book.
- Enter Ders Adı (e.g., "Matematik").
- Enter Açıklama (e.g., "Test 1 Açıklaması").
- Enter Alt İsim (e.g., "Test 1 Alt İsmi").
- Select 4.Sınıf from the Sınıf dropdown.
- Select Matetamatik from the Ders dropdown.
- Select any subject from the Konu dropdown.
- Select any sub topic from the Alt Konu dropdown.
- Click Kaydet Buton.
- After creating the test, click the "Soru Ekleme Adımına Geç" button to add a new questions.
- /questioncanvas page will be opened. Click the "Klasör Yükle" button to upload a question folder. Select the "questions" folder from the local file system and click "Open". The question folders are under "/Volumes/Mustafas HDD/HedefOkul/4/WebPImages" folder. Each folder contains a bunch of webp images. After loading the folder it will start uploading the images. Wait until all images are uploaded.

# teacher account credentials:
- username: teacher@hedefokul.com
- password: Musty1618

# rules for login
- Sometimes it logs out automatically when first login, if that happens, login again with the same credentials.
