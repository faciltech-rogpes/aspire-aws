# Docker Engine no Windows via WSL2 (sem Docker Desktop)

Guia para instalar o Docker Engine diretamente no WSL2, dispensando o Docker Desktop.
Necessário para rodar os cenários deste projeto em Windows sem licença Docker Desktop.

> **Como funciona a conexão Windows ↔ Docker:**
> O Docker Engine roda dentro do Debian (WSL2) e escuta na porta TCP `2375`.
> O WSL2 em modo NAT faz **port forwarding automático** para o Windows — enquanto o WSL2
> estiver ativo, `localhost:2375` no Windows aponta diretamente para o Docker no Debian.
> Não é necessário configurar firewall nem usar o IP interno do WSL2.

---

## Pré-requisitos

- Windows 10 (build 19041+) ou Windows 11
- .NET SDK 10+

---

## 1. Instalar o WSL2 e uma distro Debian

**Verifique se o WSL já está instalado:**

_PowerShell (Windows):_

```powershell
wsl --status
```

Se o comando não for reconhecido, instale:

_PowerShell (Windows):_

```powershell
wsl --install -d Debian
```

Se já estiver instalado, verifique se a distro Debian está presente:

_PowerShell (Windows):_

```powershell
wsl --list --verbose
```

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
> Procure por **"Debian"** no menu Iniciar e clique no app, ou execute no PowerShell:
>
> ```powershell
> wsl -d Debian
> ```
>
> O prompt muda para algo como `roger@MAQUINA:~$` — você está dentro do Linux.
>
> **Como sair e voltar ao Windows:** digite `exit` e pressione **Enter**, ou feche a janela.

Abra o terminal Debian e **verifique se o Docker já está instalado:**

_Terminal Debian (WSL2):_

```bash
docker --version
```

Se retornar uma versão (ex: `Docker version 29.x.x`), pule para o [passo 3](#3-habilitar-o-systemd-no-debian).

Caso contrário, execute:

_Terminal Debian (WSL2):_

```bash
# Remove versões antigas, se houver
sudo apt remove docker docker-engine docker.io containerd runc

# Dependências
sudo apt update
sudo apt install -y ca-certificates curl gnupg

# Repositório oficial Docker
sudo install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/debian/gpg | sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg
sudo chmod a+r /etc/apt/keyrings/docker.gpg

echo \
  "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] \
  https://download.docker.com/linux/debian $(. /etc/os-release && echo "$VERSION_CODENAME") stable" | \
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

Se retornar um número de versão, o systemd já está ativo — pule para o [passo 4](#4-configurar-o-docker-para-aceitar-conexões-tcp).

Caso contrário, ative-o editando `/etc/wsl.conf`:

_Terminal Debian (WSL2):_

```bash
sudo nano /etc/wsl.conf
```

O editor abre no terminal. Digite o conteúdo abaixo. Se o arquivo já tiver conteúdo, adicione apenas as linhas que faltarem:

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

Abra o terminal Debian novamente e confirme:

_Terminal Debian (WSL2):_

```bash
systemctl --version
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
> Nunca exponha essa porta em redes compartilhadas ou corporativas.

### 4b. Criar o override do serviço systemd

O systemd passa `-H fd://` ao iniciar o Docker, e o Docker não aceita a opção `hosts` definida
em dois lugares ao mesmo tempo. O override remove esse flag para que apenas o `daemon.json` defina as interfaces.

_Terminal Debian (WSL2):_

```bash
sudo mkdir -p /etc/systemd/system/docker.service.d
sudo nano /etc/systemd/system/docker.service.d/override.conf
```

Digite exatamente (as duas linhas `ExecStart=` são obrigatórias — a primeira limpa o valor padrão):

```ini
[Service]
ExecStart=
ExecStart=/usr/bin/dockerd
```

Salve: **Ctrl+X** → **Y** → **Enter**

### 4c. Desabilitar o docker.socket

O systemd inclui uma unidade `docker.socket` que pode ativar o Docker via socket antes do serviço
iniciar, reintroduzindo o `-H fd://` e ignorando o override. Desabilite-a:

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

Deve aparecer `Active: active (running)`. Confirme que **não** há `TriggeredBy: docker.socket`
na saída — se aparecer, o socket ainda está ativo e o passo 4c não foi aplicado corretamente.

Confirme que o processo subiu sem `-H fd://`:

_Terminal Debian (WSL2):_

```bash
ps aux | grep dockerd
```

A linha do processo deve mostrar apenas `/usr/bin/dockerd`, sem `-H fd://`.

Confirme que a porta TCP está escutando:

_Terminal Debian (WSL2):_

```bash
ss -tlnp | grep 2375
```

Deve retornar uma linha com `*:2375`. Teste o Docker localmente:

_Terminal Debian (WSL2):_

```bash
docker run hello-world
```

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

Feche e reabra o PowerShell. Verifique (com o Debian já aberto em outra janela):

_PowerShell (Windows):_

```powershell
docker info | Select-String "Server Version"
```

> **Importante:** o Docker só fica acessível enquanto o WSL2 estiver ativo.
> Antes de rodar os testes, siga o passo 6.

---

## 6. Antes de rodar os testes: iniciar o WSL2

O Docker sobe automaticamente pelo systemd quando o WSL2 inicia. **É necessário manter um terminal Debian aberto durante toda a execução dos testes** — o WSL2 fica ativo enquanto houver uma sessão aberta, e o port forwarding para `localhost:2375` depende disso.

Abra o terminal Debian por qualquer uma das opções abaixo:

---

**Menu Iniciar**

Procure por **"Debian"** e clique no app. Uma janela de terminal Linux abrirá.

---

**Git Bash**

```bash
wsl -d Debian
```

---

**PowerShell**

```powershell
wsl -d Debian
```

---

**Terminal integrado do VS Code**

Abra um novo terminal (**Ctrl+\`**), clique na seta ao lado do `+` e escolha **Debian (WSL)** na lista de perfis disponíveis.

---

Após abrir, aguarde o prompt `usuario@MAQUINA:~$` aparecer — o Docker já estará rodando. Minimize a janela e execute os testes normalmente. Não feche o terminal enquanto os testes estiverem em execução.

---

## 7. Verificar integração com o projeto

_PowerShell (Windows):_

```powershell
cd D:\Projetos\Credito\aspire-aws
dotnet test scenarios/01-S3.Basic/
```

Os 5 testes devem passar. O Aspire sobe o LocalStack e o PostgreSQL automaticamente via Docker Engine no WSL2.

---

## 8. Verificar e limpar containers após os testes

O Aspire remove os containers ao final de cada execução, mas se um teste falhar no meio do caminho containers podem ficar parados.

**Verificar containers em execução:**

_Terminal Debian (WSL2):_

```bash
docker ps
```

**Verificar todos os containers, incluindo os parados:**

_Terminal Debian (WSL2):_

```bash
docker ps -a
```

**Remover containers parados:**

_Terminal Debian (WSL2):_

```bash
docker container prune -f
```

> **Recomendado para uso cotidiano.** Remove apenas containers parados — o equivalente ao que o Aspire
> faria ao encerrar normalmente. As imagens (`localstack/localstack`, `postgres`) são preservadas,
> evitando novo download na próxima execução.

**Limpeza geral** (containers parados + imagens sem uso + redes + cache de build):

_Terminal Debian (WSL2):_

```bash
docker system prune -f
```

> **Use com cautela.** Remove as imagens não referenciadas por nenhum container, incluindo
> `localstack/localstack` e `postgres`. Na próxima execução dos testes, essas imagens serão
> baixadas novamente (pode levar alguns minutos dependendo da conexão).

> `docker system prune` não remove volumes. Para remover tudo incluindo volumes (apaga dados persistentes):
>
> ```bash
> docker system prune --volumes -f
> ```

---

## Referências

- [Documentação oficial Docker Engine — Debian](https://docs.docker.com/engine/install/debian/)
- [Documentação WSL2 — Microsoft](https://learn.microsoft.com/pt-br/windows/wsl/)
