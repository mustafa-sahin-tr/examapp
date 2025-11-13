<#import "template.ftl" as layout>
<@layout.registrationLayout>
  <link rel="stylesheet" href="${url.resourcesPath}/css/custom.css">

  <div class="login-container">
    <div class="login-card">
      <h2>Şifre Sıfırlama</h2>

      <form action="${url.loginAction}" method="post">
        <div class="form-field">
          <label for="username">E-posta adresiniz</label>
          <input id="username" name="username" type="text" autofocus required />
        </div>

        <button class="login-button" type="submit">Sıfırlama Bağlantısı Gönder</button>
      </form>

      <div class="actions">
        <a href="${url.loginUrl}">Giriş Sayfasına Dön</a>
      </div>
    </div>
  </div>
</@layout.registrationLayout>
