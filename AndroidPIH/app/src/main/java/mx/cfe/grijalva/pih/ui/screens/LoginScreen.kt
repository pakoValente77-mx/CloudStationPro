package mx.cfe.grijalva.pih.ui.screens

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusDirection
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.launch
import mx.cfe.grijalva.pih.data.service.AuthService
import mx.cfe.grijalva.pih.ui.theme.*

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun LoginScreen(authService: AuthService) {
    val isLoading by authService.isLoading.collectAsState()
    val errorMessage by authService.errorMessage.collectAsState()
    val scope = rememberCoroutineScope()
    val focusManager = LocalFocusManager.current

    var serverUrl by remember { mutableStateOf(authService.serverUrl) }
    var userName by remember { mutableStateOf("") }
    var password by remember { mutableStateOf("") }
    var showPassword by remember { mutableStateOf(false) }
    var showServerConfig by remember { mutableStateOf(false) }

    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(PihBackground)
            .padding(24.dp),
        contentAlignment = Alignment.Center
    ) {
        Column(
            horizontalAlignment = Alignment.CenterHorizontally,
            modifier = Modifier.fillMaxWidth()
        ) {
            // Logo/Title
            Text(
                text = "💧",
                fontSize = 64.sp
            )
            Spacer(modifier = Modifier.height(8.dp))
            Text(
                text = "PIH",
                fontSize = 36.sp,
                fontWeight = FontWeight.Bold,
                color = PihCyan
            )
            Text(
                text = "Plataforma Integral\nHidrometeorológica",
                fontSize = 14.sp,
                color = PihTextSecondary,
                textAlign = TextAlign.Center
            )
            Text(
                text = "CFE Subgerencia Técnica Grijalva",
                fontSize = 12.sp,
                color = PihTextSecondary.copy(alpha = 0.7f),
                modifier = Modifier.padding(top = 4.dp)
            )

            Spacer(modifier = Modifier.height(40.dp))

            // Server config button
            TextButton(onClick = { showServerConfig = !showServerConfig }) {
                Icon(
                    Icons.Default.Settings,
                    contentDescription = null,
                    tint = PihTextSecondary,
                    modifier = Modifier.size(16.dp)
                )
                Spacer(modifier = Modifier.width(4.dp))
                Text(
                    "Configurar servidor",
                    color = PihTextSecondary,
                    fontSize = 12.sp
                )
            }

            if (showServerConfig) {
                OutlinedTextField(
                    value = serverUrl,
                    onValueChange = { serverUrl = it },
                    label = { Text("URL del Servidor") },
                    leadingIcon = { Icon(Icons.Default.Cloud, contentDescription = null) },
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth(),
                    colors = OutlinedTextFieldDefaults.colors(
                        focusedBorderColor = PihPurple,
                        unfocusedBorderColor = PihDivider,
                        focusedLabelColor = PihPurple,
                        cursorColor = PihCyan,
                        focusedTextColor = PihTextPrimary,
                        unfocusedTextColor = PihTextPrimary
                    ),
                    keyboardOptions = KeyboardOptions(
                        keyboardType = KeyboardType.Uri,
                        imeAction = ImeAction.Next
                    ),
                    keyboardActions = KeyboardActions(
                        onNext = { focusManager.moveFocus(FocusDirection.Down) }
                    )
                )
                Spacer(modifier = Modifier.height(12.dp))
            }

            // Username
            OutlinedTextField(
                value = userName,
                onValueChange = { userName = it },
                label = { Text("Usuario") },
                leadingIcon = { Icon(Icons.Default.Person, contentDescription = null) },
                singleLine = true,
                modifier = Modifier.fillMaxWidth(),
                colors = OutlinedTextFieldDefaults.colors(
                    focusedBorderColor = PihPurple,
                    unfocusedBorderColor = PihDivider,
                    focusedLabelColor = PihPurple,
                    cursorColor = PihCyan,
                    focusedTextColor = PihTextPrimary,
                    unfocusedTextColor = PihTextPrimary
                ),
                keyboardOptions = KeyboardOptions(imeAction = ImeAction.Next),
                keyboardActions = KeyboardActions(
                    onNext = { focusManager.moveFocus(FocusDirection.Down) }
                )
            )

            Spacer(modifier = Modifier.height(12.dp))

            // Password
            OutlinedTextField(
                value = password,
                onValueChange = { password = it },
                label = { Text("Contraseña") },
                leadingIcon = { Icon(Icons.Default.Lock, contentDescription = null) },
                trailingIcon = {
                    IconButton(onClick = { showPassword = !showPassword }) {
                        Icon(
                            if (showPassword) Icons.Default.VisibilityOff else Icons.Default.Visibility,
                            contentDescription = null,
                            tint = PihTextSecondary
                        )
                    }
                },
                visualTransformation = if (showPassword) VisualTransformation.None else PasswordVisualTransformation(),
                singleLine = true,
                modifier = Modifier.fillMaxWidth(),
                colors = OutlinedTextFieldDefaults.colors(
                    focusedBorderColor = PihPurple,
                    unfocusedBorderColor = PihDivider,
                    focusedLabelColor = PihPurple,
                    cursorColor = PihCyan,
                    focusedTextColor = PihTextPrimary,
                    unfocusedTextColor = PihTextPrimary
                ),
                keyboardOptions = KeyboardOptions(
                    keyboardType = KeyboardType.Password,
                    imeAction = ImeAction.Done
                ),
                keyboardActions = KeyboardActions(
                    onDone = {
                        focusManager.clearFocus()
                        if (userName.isNotBlank() && password.isNotBlank()) {
                            scope.launch { authService.login(serverUrl, userName, password) }
                        }
                    }
                )
            )

            // Error message
            errorMessage?.let { msg ->
                Spacer(modifier = Modifier.height(12.dp))
                Text(
                    text = msg,
                    color = PihRed,
                    fontSize = 13.sp,
                    textAlign = TextAlign.Center,
                    modifier = Modifier.fillMaxWidth()
                )
            }

            Spacer(modifier = Modifier.height(24.dp))

            // Login button
            Button(
                onClick = {
                    focusManager.clearFocus()
                    scope.launch { authService.login(serverUrl, userName, password) }
                },
                enabled = !isLoading && userName.isNotBlank() && password.isNotBlank(),
                modifier = Modifier
                    .fillMaxWidth()
                    .height(50.dp),
                shape = RoundedCornerShape(12.dp),
                colors = ButtonDefaults.buttonColors(
                    containerColor = PihPurple
                )
            ) {
                if (isLoading) {
                    CircularProgressIndicator(
                        modifier = Modifier.size(24.dp),
                        color = PihTextPrimary,
                        strokeWidth = 2.dp
                    )
                } else {
                    Text(
                        "Iniciar Sesión",
                        fontSize = 16.sp,
                        fontWeight = FontWeight.Bold
                    )
                }
            }
        }
    }
}
