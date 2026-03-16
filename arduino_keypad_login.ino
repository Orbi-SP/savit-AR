/*
 * Arduino Keypad 4x4 - Sistema de Login
 * 
 * Este código lê o teclado 4x4 e envia os dados via Serial
 * para ser capturado pelo script Python (arduinoSerialReader.py)
 * 
 * Bibliotecas necessárias:
 * - Keypad by Mark Stanley, Alexander Brevig
 *   (Instale via: Sketch > Include Library > Manage Libraries > Buscar "Keypad")
 * 
 * Conexões do Teclado 4x4:
 * - Rows (R1-R4): Pinos 9, 8, 7, 6
 * - Cols (C1-C4): Pinos 5, 4, 3, 2
 */

#include <Keypad.h>

const byte ROWS = 4; // Quatro linhas
const byte COLS = 4; // Quatro colunas

// Define o mapa de teclas
char keys[ROWS][COLS] = {
  {'1','2','3','A'},
  {'4','5','6','B'},
  {'7','8','9','C'},
  {'*','0','#','D'}
};

// Conecta aos pinos do Arduino
byte rowPins[ROWS] = {9, 8, 7, 6}; // Pinos das linhas
byte colPins[COLS] = {5, 4, 3, 2}; // Pinos das colunas

// Cria o objeto Keypad
Keypad keypad = Keypad(makeKeymap(keys), rowPins, colPins, ROWS, COLS);

String passwordBuffer = "";
const int MAX_PASSWORD_LENGTH = 10;

void setup() {
  Serial.begin(9600);
  Serial.println("=================================");
  Serial.println("  Arduino Keypad 4x4 - Login IoT");
  Serial.println("=================================");
  Serial.println();
  Serial.println("Instruções:");
  Serial.println("- Digite a senha usando o teclado");
  Serial.println("- Pressione '#' para enviar");
  Serial.println("- Pressione '*' para limpar");
  Serial.println();
  Serial.println("Aguardando entrada...");
  Serial.println();
}

void loop() {
  char key = keypad.getKey();
  
  if (key) {
    // '#' envia a senha
    if (key == '#') {
      if (passwordBuffer.length() > 0) {
        Serial.println();
        Serial.print("Senha enviada: ");
        for (int i = 0; i < passwordBuffer.length(); i++) {
          Serial.print('*');
        }
        Serial.println();
        
        // Envia cada caractere individualmente
        for (int i = 0; i < passwordBuffer.length(); i++) {
          Serial.print(passwordBuffer.charAt(i));
        }
        Serial.print('#'); // Envia o marcador de fim
        Serial.println();
        
        passwordBuffer = "";
        Serial.println("Aguardando nova entrada...");
      } else {
        Serial.println("Senha vazia!");
      }
    }
    // '*' limpa a senha
    else if (key == '*') {
      passwordBuffer = "";
      Serial.println();
      Serial.println("Senha limpa!");
      Serial.println("Aguardando nova entrada...");
    }
    // Adiciona dígitos à senha
    else {
      if (passwordBuffer.length() < MAX_PASSWORD_LENGTH) {
        passwordBuffer += key;
        
        // Mostra feedback visual com asteriscos
        Serial.print("Senha: ");
        for (int i = 0; i < passwordBuffer.length(); i++) {
          Serial.print('*');
        }
        Serial.println();
      } else {
        Serial.println("Tamanho máximo da senha atingido!");
        Serial.println("Pressione '#' para enviar ou '*' para limpar");
      }
    }
  }
}
