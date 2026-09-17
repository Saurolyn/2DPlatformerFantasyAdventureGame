extends CharacterBody2D

var speed = 100.0
var direction = 1
var gravity = ProjectSettings.get_setting("physics/2d/default_gravity")

func _physics_process(delta):
	if not is_on_floor():
		velocity.y += gravity * delta

	# Intelligent Behavior: Reverse direction if hitting a wall or about to fall off a ledge
	if is_on_wall() or not $LedgeCheck.is_colliding():
		direction *= -1
		$WalkAnimation.flip_h = direction > 0
		$LedgeCheck.position.x *= -1 # Flip the raycast side

	velocity.x = direction * speed
	move_and_slide()

# Connect the Hitbox Area2D's body_entered signal here
func _on_hitbox_body_entered(body):
	if body.name == "Player":
		GameManager.lose_game()
