/**
 * Three.js Particle Background Component
 * Elegant, minimalist particle field for backgrounds
 */

class ParticleBackground {
    constructor(containerId, options = {}) {
        this.container = document.getElementById(containerId);
        if (!this.container) return;

        this.options = {
            particleCount: options.particleCount || 100,
            particleSize: options.particleSize || 2,
            particleColor: options.particleColor || 0x60a5fa,
            connectionDistance: options.connectionDistance || 150,
            mouseInteraction: options.mouseInteraction !== false,
            speed: options.speed || 0.0005,
            ...options
        };

        this.mouse = { x: 0, y: 0 };
        this.init();
    }

    init() {
        // Scene setup
        this.scene = new THREE.Scene();
        
        // Camera
        this.camera = new THREE.PerspectiveCamera(
            75,
            this.container.clientWidth / this.container.clientHeight,
            0.1,
            1000
        );
        this.camera.position.z = 400;

        // Renderer
        this.renderer = new THREE.WebGLRenderer({ 
            alpha: true, 
            antialias: true 
        });
        this.renderer.setSize(this.container.clientWidth, this.container.clientHeight);
        this.renderer.setPixelRatio(window.devicePixelRatio);
        this.container.appendChild(this.renderer.domElement);

        // Particles
        this.createParticles();

        // Event listeners
        this.setupEventListeners();

        // Start animation
        this.animate();
    }

    createParticles() {
        const geometry = new THREE.BufferGeometry();
        const positions = [];
        const velocities = [];

        for (let i = 0; i < this.options.particleCount; i++) {
            positions.push(
                Math.random() * 800 - 400,
                Math.random() * 800 - 400,
                Math.random() * 800 - 400
            );
            velocities.push(
                (Math.random() - 0.5) * this.options.speed,
                (Math.random() - 0.5) * this.options.speed,
                (Math.random() - 0.5) * this.options.speed
            );
        }

        geometry.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
        this.velocities = velocities;

        const material = new THREE.PointsMaterial({
            color: this.options.particleColor,
            size: this.options.particleSize,
            transparent: true,
            opacity: 0.6,
            blending: THREE.AdditiveBlending
        });

        this.particles = new THREE.Points(geometry, material);
        this.scene.add(this.particles);

        // Connection lines
        if (this.options.connectionDistance > 0) {
            this.linesMaterial = new THREE.LineBasicMaterial({
                color: this.options.particleColor,
                transparent: true,
                opacity: 0.15,
                blending: THREE.AdditiveBlending
            });
        }
    }

    updateParticles() {
        const positions = this.particles.geometry.attributes.position.array;

        for (let i = 0; i < positions.length; i += 3) {
            positions[i] += this.velocities[i];
            positions[i + 1] += this.velocities[i + 1];
            positions[i + 2] += this.velocities[i + 2];

            // Boundary check
            if (Math.abs(positions[i]) > 400) this.velocities[i] *= -1;
            if (Math.abs(positions[i + 1]) > 400) this.velocities[i + 1] *= -1;
            if (Math.abs(positions[i + 2]) > 400) this.velocities[i + 2] *= -1;

            // Mouse interaction
            if (this.options.mouseInteraction) {
                const dx = this.mouse.x - positions[i];
                const dy = this.mouse.y - positions[i + 1];
                const distance = Math.sqrt(dx * dx + dy * dy);
                
                if (distance < 100) {
                    positions[i] -= dx * 0.01;
                    positions[i + 1] -= dy * 0.01;
                }
            }
        }

        this.particles.geometry.attributes.position.needsUpdate = true;

        // Update connection lines
        if (this.options.connectionDistance > 0) {
            this.updateConnections(positions);
        }
    }

    updateConnections(positions) {
        // Remove old lines
        if (this.lines) {
            this.scene.remove(this.lines);
        }

        const linePositions = [];
        const particleCount = positions.length / 3;

        for (let i = 0; i < particleCount; i++) {
            for (let j = i + 1; j < particleCount; j++) {
                const dx = positions[i * 3] - positions[j * 3];
                const dy = positions[i * 3 + 1] - positions[j * 3 + 1];
                const dz = positions[i * 3 + 2] - positions[j * 3 + 2];
                const distance = Math.sqrt(dx * dx + dy * dy + dz * dz);

                if (distance < this.options.connectionDistance) {
                    linePositions.push(
                        positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2],
                        positions[j * 3], positions[j * 3 + 1], positions[j * 3 + 2]
                    );
                }
            }
        }

        if (linePositions.length > 0) {
            const lineGeometry = new THREE.BufferGeometry();
            lineGeometry.setAttribute('position', new THREE.Float32BufferAttribute(linePositions, 3));
            this.lines = new THREE.LineSegments(lineGeometry, this.linesMaterial);
            this.scene.add(this.lines);
        }
    }

    setupEventListeners() {
        // Mouse move
        if (this.options.mouseInteraction) {
            this.container.addEventListener('mousemove', (e) => {
                const rect = this.container.getBoundingClientRect();
                this.mouse.x = ((e.clientX - rect.left) / rect.width) * 800 - 400;
                this.mouse.y = -((e.clientY - rect.top) / rect.height) * 800 + 400;
            });
        }

        // Window resize
        window.addEventListener('resize', () => this.onWindowResize());
    }

    onWindowResize() {
        this.camera.aspect = this.container.clientWidth / this.container.clientHeight;
        this.camera.updateProjectionMatrix();
        this.renderer.setSize(this.container.clientWidth, this.container.clientHeight);
    }

    animate() {
        requestAnimationFrame(() => this.animate());
        this.updateParticles();
        this.particles.rotation.y += 0.0002;
        this.renderer.render(this.scene, this.camera);
    }

    destroy() {
        if (this.renderer) {
            this.container.removeChild(this.renderer.domElement);
            this.renderer.dispose();
        }
    }
}

// Export for use in other scripts
if (typeof module !== 'undefined' && module.exports) {
    module.exports = ParticleBackground;
}
