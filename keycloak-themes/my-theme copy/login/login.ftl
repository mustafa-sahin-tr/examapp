<#import "template.ftl" as layout>

@layout.registrationLayout bodyClass="login-page"

<h1>Welcome to Custom Login</h1>

<form id="kc-form-login" action="${url.loginAction}" method="post">
  <div>
    <label for="username">Username or email:</label>
    <input id="username" name="username" value="${login.username}" type="text" autofocus autocomplete="username" />
  </div>

  <div>
    <label for="password">Password:</label>
    <input id="password" name="password" type="password" autocomplete="current-password" />
  </div>

  <div>
    <input type="submit" value="Login" />
  </div>
</form>
