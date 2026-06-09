# Docker Engine no Windows via WSL2 (sem Docker Desktop)

Guia para instalar o Docker Engine diretamente no WSL2, dispensando o Docker Desktop.
Necessário para rodar os cenários deste projeto em Windows sem licença Docker Desktop.

> **⚠️ ATENÇÃO: PowerShell vs. CMD**
> Salvo indicação em contrário, todos os comandos do Windows neste guia devem ser executados no **PowerShell**, e não no Prompt de Comando (CMD). O PowerShell possui sintaxes específicas (como manipulação de variáveis de ambiente) que falharão no CMD padrão.

> **Como funciona a conexão Windows ↔ Docker:**
> O Docker Engine roda dentro do Debian (WSL2) e escuta na porta TCP `2375`.
> O WSL2 em modo NAT faz **port forwarding automático** para o Windows — enquanto o WSL2
> estiver ativo, `localhost:2375` no Windows aponta diretamente para o Docker no Debian.
> Não é necessário configurar firewall nem usar o IP interno do WSL2.

---

## Pré-requisitos

- Windows 10 (build 19041+) ou Windows 11
- .NET SDK 10+ (Instruções no Passo 7)

---

## 1. Instalar o WSL2 e uma distro Debian

**Verifique se o WSL já está instalado:**

_PowerShell (Windows):_

```powershell
wsl --status

```

> **Saída esperada:** Informações sobre a distribuição, como `Distribuição padrão: Debian` e `Versão Padrão: 2`. Se o comando não for reconhecido, instale usando o comando abaixo.

_PowerShell (Windows):_

```powershell
wsl --install -d Debian
```

Se já estiver instalado, verifique se a distro Debian está presente:

_PowerShell (Windows):_

```powershell
wsl --list --verbose

```

> **Saída esperada:** Uma lista contendo `Debian` com o status `Running` ou `Stopped` e a versão `2`.

Caso Debian não apareça na lista, instale-a:

_PowerShell (Windows):_

```powershell
wsl --install -d Debian
```

Garanta que a versão padrão é 2. Se alguma distro aparecer com `VERSION 1`, converta:

_PowerShell (Windows):_

```powershell
wsl --set-default-version 2
wsl --set-version Debian 2
```

Após a instalação, reinicie o Windows se solicitado, depois prossiga.

---

## 2. Instalar o Docker Engine dentro do Debian

> **Como abrir o terminal Debian:**
> Procure por **"Debian"** no menu Iniciar e clique no app, ou execute no PowerShell: `wsl -d Debian`
> O prompt muda para algo como `roger@MAQUINA:~$` — você está dentro do Linux.
> **Como sair e voltar ao Windows:** digite `exit` e pressione **Enter**, ou feche a janela.

Abra o terminal Debian e **verifique se o Docker já está instalado:**

_Terminal Debian (WSL2):_

```bash
docker --version

```

> **Saída esperada:** Algo como `Docker version 29.x.x, build...`. Se retornar a versão, pule para o [passo 3](https://www.google.com/search?q=%233-habilitar-o-systemd-no-debian). Caso retorne erro (comando não encontrado), siga abaixo.

_Terminal Debian (WSL2):_

```bash
# Remove versões antigas, se houver
sudo apt remove docker docker-engine docker.io containerd runc

# Dependências
sudo apt update
sudo apt install -y ca-certificates curl gnupg

# Repositório oficial Docker
sudo install -m 0755 -d /etc/apt/keyrings
curl -fsSL [https://download.docker.com/linux/debian/gpg](https://download.docker.com/linux/debian/gpg) | sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg
sudo chmod a+r /etc/apt/keyrings/docker.gpg

echo \
  "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] \
  [https://download.docker.com/linux/debian](https://download.docker.com/linux/debian) $(. /etc/os-release && echo "$VERSION_CODENAME") stable" | \
  sudo tee /etc/apt/sources.list.d/docker.list > /dev/null

# Instala o Docker Engine
sudo apt update
sudo apt install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
```

Adicione seu usuário ao grupo Docker para não precisar de `sudo` a cada comando:

_Terminal Debian (WSL2):_

```bash
sudo usermod -aG docker $USER
newgrp docker
```

---

## 3. Habilitar o systemd no Debian

O systemd é necessário para que o Docker inicie automaticamente quando o WSL2 sobe.

Verifique se já está ativo:

_Terminal Debian (WSL2):_

```bash
systemctl --version

```

> **Saída esperada:** `systemd 252 (252.x-x...)` ou um número de versão similar. Se retornar, pule para o [passo 4](https://www.google.com/search?q=%234-configurar-o-docker-para-aceitar-conex%C3%B5es-tcp).

Caso contrário, ative-o editando `/etc/wsl.conf`:

_Terminal Debian (WSL2):_

```bash
sudo nano /etc/wsl.conf

```

Digite o conteúdo abaixo. Se o arquivo já tiver conteúdo, adicione apenas as linhas que faltarem:

```ini
[boot]
systemd=true
```

Para salvar e sair do `nano`:

1. Pressione **Ctrl+X**
2. Pressione **Y** para confirmar
3. Pressione **Enter** para manter o nome do arquivo

Reinicie o WSL para aplicar:

_PowerShell (Windows):_

```powershell
wsl --shutdown
```

---

## 4. Configurar o Docker para aceitar conexões TCP

O .NET Aspire roda no Windows e precisa alcançar o Docker Engine no Debian via TCP.

### 4a. Criar o arquivo de configuração do daemon

_Terminal Debian (WSL2):_

```bash
sudo bash -c 'cat > /etc/docker/daemon.json << EOF
{
  "hosts": ["unix:///var/run/docker.sock", "tcp://0.0.0.0:2375"]
}
EOF'

```

> **Aviso de segurança:** a porta 2375 não usa TLS. Use apenas em ambiente de desenvolvimento local.

### 4b. Criar o override do serviço systemd

O systemd passa `-H fd://` ao iniciar o Docker, e o Docker não aceita a opção `hosts` definida em dois lugares ao mesmo tempo. O override remove esse flag.

_Terminal Debian (WSL2):_

```bash
sudo mkdir -p /etc/systemd/system/docker.service.d
sudo nano /etc/systemd/system/docker.service.d/override.conf

```

Digite exatamente (as duas linhas `ExecStart=` são obrigatórias):

```ini
[Service]
ExecStart=
ExecStart=/usr/bin/dockerd
```

Salve: **Ctrl+X** → **Y** → **Enter**

### 4c. Desabilitar o docker.socket

_Terminal Debian (WSL2):_

```bash
sudo systemctl disable docker.socket
sudo systemctl stop docker.socket
```

### 4d. Aplicar e habilitar o serviço

_Terminal Debian (WSL2):_

```bash
sudo systemctl daemon-reload
sudo systemctl enable docker
sudo systemctl start docker
```

Verifique:

_Terminal Debian (WSL2):_

```bash
sudo systemctl status docker

```

> **Saída esperada:** `Active: active (running)`. (Nota: A mensagem `TriggeredBy: docker.socket` pode aparecer em alguns cenários funcionais, o importante é que o status esteja como _running_).

Confirme que o processo subiu sem `-H fd://`:

_Terminal Debian (WSL2):_

```bash
ps aux | grep dockerd

```

> **Saída esperada:** `/usr/bin/dockerd` (sem o texto `-H fd://`).

Confirme que a porta TCP está escutando:

_Terminal Debian (WSL2):_

```bash
ss -tlnp | grep 2375

```

> **Saída esperada:** Uma linha contendo `*:2375`.

---

## 5. Configurar o Windows para se conectar ao Docker

### 5a. Instalar o Docker CLI no Windows

_PowerShell (Windows):_

```powershell
winget install Docker.DockerCLI
```

Feche e reabra o PowerShell após a instalação.

### 5b. Definir a variável DOCKER_HOST

_PowerShell (Windows):_

```powershell
[Environment]::SetEnvironmentVariable("DOCKER_HOST", "tcp://localhost:2375", "User")

```

> **Alternativa CMD (DOS):** Caso esteja usando o Prompt de Comando padrão em vez do PowerShell, utilize o comando: `setx DOCKER_HOST "tcp://localhost:2375"`

Feche e reabra o PowerShell. Verifique (com o Debian já aberto em outra janela):

_PowerShell (Windows):_

```powershell
docker info | Select-String "Server Version"

```

---

## 6. Antes de rodar os testes: iniciar o WSL2

O Docker sobe automaticamente pelo systemd quando o WSL2 inicia. **É necessário manter um terminal Debian aberto durante toda a execução dos testes**.

Para maior praticidade durante o desenvolvimento, use o **Terminal integrado do VS Code**:

1. Abra um novo terminal (**Ctrl+`**).
2. Clique na seta ao lado do `+` (canto superior direito do painel de terminal).
3. Escolha **Debian (WSL)** ou **Git Bash** e abra o Debian por ele.
4. Aguarde o prompt `usuario@MAQUINA:~$` aparecer. O Docker já estará rodando em background.

---

## 7. Preparar Ambiente e Verificar Integração

### 7a. Instalar .NET 10 SDK e Workload Aspire

Para compilar e orquestrar os testes, é obrigatório possuir o .NET 10 SDK e o workload do Aspire.

_PowerShell (Windows) - Execute como Administrador se necessário:_

```powershell
winget install Microsoft.DotNet.SDK.10

```

Após instalar, **feche e reabra o PowerShell ou o VS Code** para recarregar as variáveis de ambiente. Em seguida, instale o workload:

_PowerShell (Windows):_

```powershell
dotnet workload install aspire

```

### 7b. Rodar os testes

Com o terminal Debian (WSL2) rodando em background e as dependências instaladas, navegue até a pasta do projeto:

_PowerShell (Windows):_

```powershell
cd D:\Projetos\Credito\aspire-aws
dotnet test scenarios/01-S3.Basic/
```

Os 5 testes devem passar. O Aspire sobe o LocalStack e o PostgreSQL automaticamente via Docker Engine no WSL2.

---

## 8. Verificar e limpar containers após os testes

O Aspire remove os containers ao final de cada execução, mas se um teste falhar no meio do caminho, containers podem ficar parados.

**Remover containers parados (Recomendado):**

_Terminal Debian (WSL2):_

```bash
docker container prune -f

```

**Limpeza geral (Use com cautela):**

_Terminal Debian (WSL2):_

```bash
docker system prune -f

```

---

## Referências

- [Documentação oficial Docker Engine — Debian](https://docs.docker.com/engine/install/debian/)
- [Documentação WSL2 — Microsoft](https://learn.microsoft.com/pt-br/windows/wsl/)

```

```
